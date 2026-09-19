using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.IO;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SkinTestMenu", "local", "2.0.0")]
    [Description("Menu de spawn de items por categorias y menu de skins del item en la mano")]
    public class SkinTestMenu : RustPlugin
    {
        // ─────────────────────────────────────────────────────────────
        //  Constantes de UI
        // ─────────────────────────────────────────────────────────────
        const string UiRoot = "stm.root";
        const string UiMain = "stm.main";
        const string UiGrid = "stm.grid";
        const string UiSkinRoot = "stm.skinroot";
        const string UiSkinMain = "stm.skinmain";
        const string UiSkinGrid = "stm.skingrid";

        const int Cols = 7;
        const int Rows = 4;
        const int PerPage = Cols * Rows;   // 28

        // Un clic nunca crea mas de 30 stacks (lo que cabe en el inventario):
        // "rifle x1000" serian 1000 entidades tiradas en el suelo.
        const int MaxStacksPorClic = 30;
        const int MaxCantidad = 1000000;
        const float SegundosConfirmar = 4f;

        const string ColBackdrop = "0 0 0 0.55";
        const string ColBack = "0.11 0.115 0.13 0.98";
        const string ColHeader = "0.075 0.08 0.095 1";
        const string ColAccent = "0.85 0.45 0.18 1";
        const string ColPanel = "0.15 0.155 0.18 0.95";
        const string ColCell = "0.20 0.205 0.235 0.92";
        const string ColInput = "0.07 0.075 0.09 1";
        const string ColBtn = "0.26 0.28 0.32 0.95";
        const string ColBtnOff = "0.17 0.175 0.20 0.8";
        const string ColBtnOn = "0.80 0.42 0.16 0.95";
        const string ColGreen = "0.28 0.52 0.30 0.95";
        const string ColClose = "0.60 0.22 0.20 0.95";
        const string ColWarn = "0.92 0.26 0.16 1";
        const string ColText = "0.92 0.92 0.94 1";
        const string ColDim = "0.62 0.62 0.67 1";
        const string ColSoft = "0.42 0.42 0.47 1";

        static readonly Dictionary<string, string> NombresCategoria = new Dictionary<string, string>
        {
            { "Weapon", "Armas" }, { "Construction", "Construcción" }, { "Items", "Objetos" },
            { "Resources", "Recursos" }, { "Attire", "Ropa" }, { "Tool", "Herramientas" },
            { "Medical", "Medicina" }, { "Food", "Comida" }, { "Ammunition", "Munición" },
            { "Traps", "Trampas" }, { "Misc", "Varios" }, { "Common", "Común" },
            { "Component", "Componentes" }, { "Electrical", "Electricidad" }, { "Fun", "Diversión" },
            { "Favourite", "Favoritos" }, { "Search", "Búsqueda" }, { "All", "Todo" }
        };

        // ─────────────────────────────────────────────────────────────
        //  Estado por jugador
        // ─────────────────────────────────────────────────────────────
        class State
        {
            public string Category = "@todos";
            public int Page;
            public string Search = "";
            public int Amount = 1;              // -1 = stack completo
            public int SkinPage;
            public string SkinSearch = "";
            public int SkinDarItemId;           // 0 = skins del item en la mano; si no, dar ese item con la skin
            public ulong UltimaSkinDada;
            public readonly List<int> Recientes = new List<int>();
            public float ConfirmarLimpiarHasta;
            public bool MenuAbierto;
        }

        readonly Dictionary<ulong, State> _state = new Dictionary<ulong, State>();

        State St(BasePlayer p)
        {
            State s;
            if (!_state.TryGetValue(p.userID, out s)) _state[p.userID] = s = new State();
            return s;
        }

        // ─────────────────────────────────────────────────────────────
        //  Caches
        // ─────────────────────────────────────────────────────────────
        Dictionary<string, List<KeyValuePair<ulong, string>>> _skinsByItem;
        List<string> _categories;
        Dictionary<string, int> _itemsPorCategoria;

        void OnServerInitialized()
        {
            BuildCategories();
            BuildSkinCache();
            Puts("Menus listos: " + ItemManager.itemList.Count + " items, "
                 + _skinsByItem.Values.Sum(l => l.Count) + " skins sobre "
                 + _skinsByItem.Count + " items distintos.");
        }

        void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UiRoot);
                CuiHelper.DestroyUi(p, UiSkinRoot);
            }
        }

        void BuildCategories()
        {
            _itemsPorCategoria = ItemManager.itemList
                .GroupBy(i => i.category.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            _categories = _itemsPorCategoria.Keys
                .OrderBy(NombreCategoria)
                .ToList();
        }

        static string NombreCategoria(string cat)
        {
            string n;
            return NombresCategoria.TryGetValue(cat, out n) ? n : cat;
        }

        void BuildSkinCache()
        {
            _skinsByItem = new Dictionary<string, List<KeyValuePair<ulong, string>>>();

            var exact = new Dictionary<string, string>();
            foreach (var def in ItemManager.itemList) exact[def.shortname] = def.shortname;

            var sinWorkshop = 0;

            // ── Skins del workshop ──────────────────────────────────────
            // OJO: hay que usar WorkshopdId (3601703973), NO InventoryId (66311).
            // El cliente descarga el bundle con WorkshopSkin.LoadFromWorkshop(workshopId);
            // si le pasas el InventoryId se queda cargando para siempre porque ese
            // item del workshop no existe.
            foreach (var kv in Rust.Workshop.Approved.All)
            {
                var info = kv.Value;
                if (info.Skinnable == null) continue;

                if (info.WorkshopdId == 0UL) { sinWorkshop++; continue; }

                var shortname = ResolveShortname(info.Skinnable.ItemName, exact);
                if (shortname == null) continue;

                var nombre = string.IsNullOrEmpty(info.Name) ? ("Skin " + info.WorkshopdId) : info.Name;
                Anadir(shortname, info.WorkshopdId, nombre);
            }

            // ── Skins integradas en el juego (no necesitan descarga) ────
            // ItemSkinDirectory usa su propio espacio de IDs pequenos (101, 10001...).
            try
            {
                var dir = ItemSkinDirectory.Instance;
                if (dir != null && dir.skins != null)
                {
                    foreach (var sk in dir.skins)
                    {
                        if (!sk.isSkin || sk.id <= 0) continue;
                        var def = ItemManager.FindItemDefinition(sk.itemid);
                        if (def == null) continue;
                        Anadir(def.shortname, (ulong)sk.id, NombreSkinIntegrada(sk.name, sk.id));
                    }
                }
            }
            catch (Exception e) { PrintWarning("ItemSkinDirectory: " + e.Message); }

            foreach (var list in _skinsByItem.Values)
                list.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));

            if (sinWorkshop > 0)
                Puts(sinWorkshop + " skins aprobadas sin workshop id (no se pueden mostrar), omitidas.");
        }

        void Anadir(string shortname, ulong skinId, string nombre)
        {
            List<KeyValuePair<ulong, string>> list;
            if (!_skinsByItem.TryGetValue(shortname, out list))
                _skinsByItem[shortname] = list = new List<KeyValuePair<ulong, string>>();

            for (var i = 0; i < list.Count; i++)
                if (list[i].Key == skinId) return;   // ya esta

            list.Add(new KeyValuePair<ulong, string>(skinId, nombre));
        }

        // "assets/prefabs/clothes/tshirt/red/tshirt.red.itemskin.asset" -> "Tshirt Red"
        static string NombreSkinIntegrada(string ruta, int id)
        {
            if (string.IsNullOrEmpty(ruta)) return "Skin " + id;
            var n = ruta;
            var barra = n.LastIndexOf('/');
            if (barra >= 0) n = n.Substring(barra + 1);
            n = n.Replace(".itemskin.asset", "").Replace(".sitem.asset", "").Replace('.', ' ');
            if (n.Length == 0) return "Skin " + id;
            return char.ToUpper(n[0]) + n.Substring(1);
        }

        // "smg.thompson" casa directo; "lr300.item" hay que resolverlo
        static string ResolveShortname(string itemName, Dictionary<string, string> exact)
        {
            if (string.IsNullOrEmpty(itemName)) return null;
            if (exact.ContainsKey(itemName)) return itemName;

            var baseName = itemName;
            var sufijos = new[] { ".item", ".entity", ".deployed" };
            for (var i = 0; i < sufijos.Length; i++)
                if (baseName.EndsWith(sufijos[i]))
                    baseName = baseName.Substring(0, baseName.Length - sufijos[i].Length);

            if (exact.ContainsKey(baseName)) return baseName;

            foreach (var sn in exact.Keys)
                if (sn.EndsWith("." + baseName)) return sn;

            return null;
        }

        int NumSkins(ItemDefinition def)
        {
            List<KeyValuePair<ulong, string>> l;
            return _skinsByItem.TryGetValue(def.shortname, out l) ? l.Count : 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Alimentar al plugin Skins (/skin)
        //
        //  Skins de misticos no trae ninguna skin: espera que se las den
        //  por su config o por este hook. Sin esto, /skin abre la caja vacia.
        // ─────────────────────────────────────────────────────────────
        void OnSkinsFetch(BasePlayer player, ItemDefinition info, List<ulong> skins)
        {
            if (info == null || skins == null || _skinsByItem == null) return;

            List<KeyValuePair<ulong, string>> list;
            if (!_skinsByItem.TryGetValue(info.shortname, out list)) return;

            for (var i = 0; i < list.Count; i++)
                skins.Add(list[i].Key);
        }

        // ─────────────────────────────────────────────────────────────
        //  Comandos de chat
        // ─────────────────────────────────────────────────────────────
        [ChatCommand("menu")]
        void CmdMenu(BasePlayer player, string cmd, string[] args) { AbrirItems(player); }

        [ChatCommand("items")]
        void CmdItems(BasePlayer player, string cmd, string[] args) { AbrirItems(player); }

        [ChatCommand("sk")]
        void CmdSk(BasePlayer player, string cmd, string[] args) { AbrirSkins(player); }

        [ChatCommand("skinmenu")]
        void CmdSkinMenu(BasePlayer player, string cmd, string[] args) { AbrirSkins(player); }

        [ChatCommand("limpiar")]
        void CmdLimpiar(BasePlayer player, string cmd, string[] args) => LimpiarInventario(player);

        void AbrirItems(BasePlayer player)
        {
            St(player).Page = 0;
            DibujarItems(player);
        }

        void AbrirSkins(BasePlayer player)
        {
            if (ItemEnMano(player) == null)
            {
                player.ChatMessage("<color=#e88>Coge un item en la mano primero.</color>");
                return;
            }
            var s = St(player);
            s.SkinDarItemId = 0;
            s.SkinPage = 0;
            s.SkinSearch = "";
            DibujarSkins(player);
        }

        static Item ItemEnMano(BasePlayer player)
        {
            return player.GetActiveItem();
        }

        static string NombreDe(ItemDefinition def)
        {
            if (def == null) return "?";
            if (def.displayName != null && !string.IsNullOrEmpty(def.displayName.english))
                return def.displayName.english;
            return def.shortname;
        }

        // ─────────────────────────────────────────────────────────────
        //  Dar items
        // ─────────────────────────────────────────────────────────────
        // Reparte la cantidad en stacks validos: "rifle x10" son 10 rifles,
        // no un rifle con amount=10. Devuelve lo que se ha dado de verdad.
        static int DarItem(BasePlayer p, ItemDefinition def, int cantidad, ulong skin)
        {
            var stack = Math.Max(1, def.stackable);
            cantidad = Math.Min(cantidad, stack * MaxStacksPorClic);
            for (var resto = cantidad; resto > 0; resto -= stack)
            {
                var item = ItemManager.Create(def, Math.Min(resto, stack), skin);
                if (item != null) p.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            }
            return cantidad;
        }

        static int CantidadPara(State s, ItemDefinition def)
        {
            return s.Amount == -1 ? Math.Max(1, def.stackable) : s.Amount;
        }

        static void AnadirReciente(State s, int itemid)
        {
            s.Recientes.Remove(itemid);
            s.Recientes.Insert(0, itemid);
            if (s.Recientes.Count > PerPage) s.Recientes.RemoveAt(PerPage);
        }

        // Borra todo: inventario, cinturon y ropa
        void LimpiarInventario(BasePlayer p)
        {
            p.inventory.Strip();
            p.ChatMessage("<color=#8cf>[Creativo]</color> Inventario limpio.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Piezas de UI
        // ─────────────────────────────────────────────────────────────
        // Siempre con cultura invariante: en un Windows en espanol "0.5"
        // saldria como "0,5" y la UI se descuadraria.
        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string P(float x, float y) => F(x) + " " + F(y);

        static void Panel(CuiElementContainer c, string parent, string color, string min, string max, string name = null)
        {
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
        }

        static void Texto(CuiElementContainer c, string parent, string text, int size, TextAnchor align,
                          string color, string min, string max)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        static void Boton(CuiElementContainer c, string parent, string text, string cmd, string color,
                          string min, string max, int size = 12, string textColor = ColText)
        {
            c.Add(new CuiButton
            {
                Button = { Color = color, Command = cmd },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = textColor },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        static void Entrada(CuiElementContainer c, string parent, string name, string text, string cmd,
                            string min, string max, int chars, string placeholder, TextAnchor align)
        {
            Panel(c, parent, ColInput, min, max, name);
            // El placeholder va debajo del input para no robarle los clics
            if (string.IsNullOrEmpty(text) && placeholder != null)
                Texto(c, name, placeholder, 11, align, ColSoft, "0.04 0", "0.96 1");
            c.Add(new CuiElement
            {
                Parent = name,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = text, FontSize = 13, Align = align, Color = ColText,
                        CharsLimit = chars, Command = cmd, NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.96 1" }
                }
            });
        }

        // Fondo oscurecido a pantalla completa (clic fuera = cerrar) + panel principal
        static void Fondo(CuiElementContainer c, string root, string main, string cmdCerrar)
        {
            c.Add(new CuiPanel
            {
                Image = { Color = ColBackdrop },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", root);
            Boton(c, root, "", cmdCerrar, "0 0 0 0", "0 0", "1 1");
            Panel(c, root, ColBack, "0.06 0.07", "0.94 0.93", main);
        }

        static void Cabecera(CuiElementContainer c, string main, string titulo, string subtitulo)
        {
            Panel(c, main, ColHeader, "0 0.93", "1 1", main + ".head");
            Panel(c, main, ColAccent, "0 0.926", "1 0.93");
            Texto(c, main + ".head",
                  titulo + "   <size=12><color=#9a9aa4>" + subtitulo + "</color></size>",
                  18, TextAnchor.MiddleLeft, ColText, "0.012 0", "0.68 1");
        }

        static void Paginacion(CuiElementContainer c, string main, int page, int maxPage, string cmd)
        {
            var ant = page > 0;
            var sig = page < maxPage;

            Boton(c, main, "«", ant ? cmd + " 0" : "", ant ? ColBtn : ColBtnOff,
                  "0.675 0.015", "0.71 0.07", 16, ant ? ColText : ColSoft);
            Boton(c, main, "‹  ANTERIOR", ant ? cmd + " " + (page - 1) : "", ant ? ColBtn : ColBtnOff,
                  "0.714 0.015", "0.80 0.07", 12, ant ? ColText : ColSoft);
            Texto(c, main, (page + 1) + " / " + (maxPage + 1), 13, TextAnchor.MiddleCenter, ColText,
                  "0.80 0.015", "0.866 0.07");
            Boton(c, main, "SIGUIENTE  ›", sig ? cmd + " " + (page + 1) : "", sig ? ColBtn : ColBtnOff,
                  "0.866 0.015", "0.952 0.07", 12, sig ? ColText : ColSoft);
            Boton(c, main, "»", sig ? cmd + " " + maxPage : "", sig ? ColBtn : ColBtnOff,
                  "0.956 0.015", "0.992 0.07", 16, sig ? ColText : ColSoft);
        }

        // Rectangulo de la celda i dentro de una rejilla Cols x Rows
        static void Celda(int i, float margenX, out string min, out string max)
        {
            var ancho = 1f / Cols;
            var alto = 1f / Rows;
            var x1 = (i % Cols) * ancho;
            var y2 = 1f - (i / Cols) * alto;
            min = P(x1 + margenX, y2 - alto + 0.012f);
            max = P(x1 + ancho - margenX, y2 - 0.012f);
        }

        // ─────────────────────────────────────────────────────────────
        //  MENU DE ITEMS
        // ─────────────────────────────────────────────────────────────
        List<ItemDefinition> FiltrarItems(State s)
        {
            if (!string.IsNullOrEmpty(s.Search))
            {
                var needle = s.Search.ToLower();
                return ItemManager.itemList
                    .Select(i => new KeyValuePair<ItemDefinition, int>(i, Relevancia(i, needle)))
                    .Where(k => k.Value < 3)
                    .OrderBy(k => k.Value)
                    .ThenBy(k => NombreDe(k.Key))
                    .Select(k => k.Key)
                    .ToList();
            }

            if (s.Category == "@recientes")
                return s.Recientes
                    .Select(id => ItemManager.FindItemDefinition(id))
                    .Where(d => d != null)
                    .ToList();

            IEnumerable<ItemDefinition> q = ItemManager.itemList;
            if (s.Category == "@skins")
                q = q.Where(i => _skinsByItem.ContainsKey(i.shortname));
            else if (s.Category != "@todos")
                q = q.Where(i => i.category.ToString() == s.Category);

            return q.OrderBy(i => NombreDe(i)).ToList();
        }

        // 0 = coincidencia exacta (nombre, shortname o itemid), 1 = empieza por, 2 = contiene, 3 = nada
        static int Relevancia(ItemDefinition i, string needle)
        {
            var sn = i.shortname.ToLower();
            var nombre = NombreDe(i).ToLower();
            if (sn == needle || nombre == needle || i.itemid.ToString() == needle) return 0;
            if (sn.StartsWith(needle) || nombre.StartsWith(needle)) return 1;
            if (sn.Contains(needle) || nombre.Contains(needle)) return 2;
            return 3;
        }

        void DibujarItems(BasePlayer player)
        {
            var s = St(player);
            s.MenuAbierto = true;
            CuiHelper.DestroyUi(player, UiRoot);
            CuiHelper.AddUi(player, ConstruirItems(s));
        }

        CuiElementContainer ConstruirItems(State s)
        {
            var items = FiltrarItems(s);
            var maxPage = Math.Max(0, (items.Count - 1) / PerPage);
            if (s.Page > maxPage) s.Page = maxPage;
            if (s.Page < 0) s.Page = 0;

            var c = new CuiElementContainer();
            Fondo(c, UiRoot, UiMain, "stm.close");

            // ── cabecera
            string sub;
            if (!string.IsNullOrEmpty(s.Search)) sub = items.Count + " resultados para \"" + s.Search + "\"";
            else if (s.Category == "@recientes") sub = items.Count + " recientes";
            else sub = items.Count + " items";
            Cabecera(c, UiMain, "SPAWN DE ITEMS", sub);

            var confirmando = Time.realtimeSinceStartup < s.ConfirmarLimpiarHasta;
            Boton(c, UiMain + ".head",
                  confirmando ? "¿SEGURO? CLIC OTRA VEZ" : "LIMPIAR INVENTARIO", "stm.clearinv",
                  confirmando ? ColWarn : ColClose, "0.68 0.16", "0.862 0.84", 11);
            Boton(c, UiMain + ".head", "CERRAR  ×", "stm.close", ColBtn, "0.87 0.16", "0.992 0.84", 12);

            // ── barra lateral de categorias
            Panel(c, UiMain, ColPanel, "0.008 0.015", "0.15 0.915", "stm.side");

            var tabs = new List<string> { "@todos", "@recientes", "@skins" };
            tabs.AddRange(_categories);

            var alto = 1f / Math.Max(tabs.Count, 1);
            for (var i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                string etiqueta;
                if (tab == "@todos") etiqueta = "TODOS  <color=#8a8a94>" + ItemManager.itemList.Count + "</color>";
                else if (tab == "@recientes") etiqueta = "RECIENTES  <color=#8a8a94>" + s.Recientes.Count + "</color>";
                else if (tab == "@skins") etiqueta = "CON SKINS  <color=#8a8a94>" + _skinsByItem.Count + "</color>";
                else etiqueta = NombreCategoria(tab) + "  <color=#8a8a94>" + _itemsPorCategoria[tab] + "</color>";

                var activo = string.IsNullOrEmpty(s.Search) && s.Category == tab;
                var y1 = 1f - (i + 1) * alto;
                var y2 = 1f - i * alto;
                Boton(c, "stm.side", etiqueta, "stm.cat " + tab, activo ? ColBtnOn : ColCell,
                      P(0.05f, y1 + 0.003f), P(0.95f, y2 - 0.003f), 11);
            }

            // ── buscador
            Entrada(c, UiMain, "stm.searchbox", s.Search, "stm.search", "0.16 0.848", "0.50 0.905",
                    40, "Buscar por nombre, shortname o ID y pulsa Enter...", TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(s.Search))
                Boton(c, UiMain, "×", "stm.cat @todos", ColClose, "0.503 0.848", "0.53 0.905", 12);

            // ── selector de cantidad
            Texto(c, UiMain, "CANTIDAD", 11, TextAnchor.MiddleRight, ColDim, "0.535 0.848", "0.595 0.905");

            var cantidades = new[] { 1, 10, 100, 1000 };
            var x = 0.60f;
            for (var i = 0; i < cantidades.Length; i++, x += 0.055f)
            {
                var n = cantidades[i];
                Boton(c, UiMain, "x" + n, "stm.amount " + n, s.Amount == n ? ColBtnOn : ColCell,
                      P(x, 0.848f), P(x + 0.051f, 0.905f), 12);
            }
            Boton(c, UiMain, "STACK", "stm.amount -1", s.Amount == -1 ? ColBtnOn : ColCell,
                  P(x, 0.848f), P(x + 0.051f, 0.905f), 11);
            x += 0.055f;

            var personalizada = s.Amount != -1 && Array.IndexOf(cantidades, s.Amount) < 0;
            Entrada(c, UiMain, "stm.amountbox", personalizada ? s.Amount.ToString() : "", "stm.amount",
                    P(x, 0.848f), "0.992 0.905", 7, "otra...", TextAnchor.MiddleCenter);
            if (personalizada)
                Panel(c, UiMain, ColAccent, P(x, 0.842f), "0.992 0.846");

            // ── rejilla
            Panel(c, UiMain, "0 0 0 0", "0.16 0.085", "0.992 0.835", UiGrid);

            if (items.Count == 0)
            {
                Texto(c, UiGrid, s.Category == "@recientes" && string.IsNullOrEmpty(s.Search)
                        ? "Aún no has sacado nada. Los items que spawnees aparecerán aquí."
                        : "No hay items que coincidan.",
                      16, TextAnchor.MiddleCenter, ColDim, "0 0", "1 1");
            }

            for (var i = 0; i < PerPage; i++)
            {
                var idx = s.Page * PerPage + i;
                if (idx >= items.Count) break;
                var def = items[idx];

                string min, max;
                Celda(i, 0.005f, out min, out max);
                var cell = "stm.cell." + i;
                Panel(c, UiGrid, ColCell, min, max, cell);

                c.Add(new CuiElement
                {
                    Parent = cell,
                    Components =
                    {
                        new CuiImageComponent { ItemId = def.itemid },
                        new CuiRectTransformComponent { AnchorMin = "0.2 0.36", AnchorMax = "0.8 0.95" }
                    }
                });

                Texto(c, cell, NombreDe(def), 10, TextAnchor.MiddleCenter, ColText, "0.03 0.17", "0.97 0.37");

                if (def.stackable > 1)
                    Texto(c, cell, "x" + def.stackable, 9, TextAnchor.UpperRight, ColSoft, "0.5 0.8", "0.96 0.97");

                // Toda la celda: recibir el item
                Boton(c, cell, "", "stm.give " + def.itemid, "0 0 0 0", "0 0", "1 1");

                // Encima: la pastilla de skins abre el selector para recibirlo ya con skin
                var nSkins = NumSkins(def);
                if (nSkins > 0)
                    Boton(c, cell, "ELEGIR SKIN  <color=#cfe8d0>(" + nSkins + ")</color>", "stm.skinsof " + def.itemid,
                          ColGreen, "0.06 0.03", "0.94 0.16", 9);
                else
                    Texto(c, cell, def.shortname, 9, TextAnchor.MiddleCenter, ColSoft, "0.03 0.03", "0.97 0.16");
            }

            // ── pie
            Texto(c, UiMain, "Clic en un item para recibirlo   ·   ELEGIR SKIN: te lo da con la skin   ·   /sk: skins del item en la mano",
                  10, TextAnchor.MiddleLeft, ColDim, "0.16 0.015", "0.67 0.07");
            Paginacion(c, UiMain, s.Page, maxPage, "stm.page");

            return c;
        }

        // ─────────────────────────────────────────────────────────────
        //  MENU DE SKINS
        //   - desde /sk: aplica la skin al item que llevas en la mano
        //   - desde ELEGIR SKIN del /menu: te da el item con esa skin
        // ─────────────────────────────────────────────────────────────
        void DibujarSkins(BasePlayer player)
        {
            var s = St(player);
            ItemDefinition def;
            ulong actual;

            if (s.SkinDarItemId != 0)
            {
                def = ItemManager.FindItemDefinition(s.SkinDarItemId);
                actual = s.UltimaSkinDada;
            }
            else
            {
                var item = ItemEnMano(player);
                if (item == null)
                {
                    CuiHelper.DestroyUi(player, UiSkinRoot);
                    player.ChatMessage("<color=#e88>Coge un item en la mano primero.</color>");
                    return;
                }
                def = item.info;
                actual = item.skin;
            }
            if (def == null) return;

            CuiHelper.DestroyUi(player, UiSkinRoot);
            CuiHelper.AddUi(player, ConstruirSkins(def, actual, s));
        }

        CuiElementContainer ConstruirSkins(ItemDefinition def, ulong skinActual, State s)
        {
            var modoDar = s.SkinDarItemId != 0;

            List<KeyValuePair<ulong, string>> todas;
            if (!_skinsByItem.TryGetValue(def.shortname, out todas))
                todas = new List<KeyValuePair<ulong, string>>();

            var lista = todas;
            if (!string.IsNullOrEmpty(s.SkinSearch))
            {
                var needle = s.SkinSearch.ToLower();
                lista = todas.Where(k => k.Value.ToLower().Contains(needle)
                                      || k.Key.ToString().Contains(needle)).ToList();
            }

            var maxPage = Math.Max(0, (lista.Count - 1) / PerPage);
            if (s.SkinPage > maxPage) s.SkinPage = maxPage;
            if (s.SkinPage < 0) s.SkinPage = 0;

            var c = new CuiElementContainer();
            Fondo(c, UiSkinRoot, UiSkinMain, "stm.closeskin");

            Cabecera(c, UiSkinMain, "SKINS DE: " + NombreDe(def).ToUpper(),
                     lista.Count + (lista.Count == todas.Count ? "" : " de " + todas.Count) + " skins");
            Boton(c, UiSkinMain + ".head", modoDar ? "‹  VOLVER" : "CERRAR  ×", "stm.closeskin", ColBtn,
                  "0.87 0.16", "0.992 0.84", 12);

            // ── barra de herramientas
            Entrada(c, UiSkinMain, "stm.sksearchbox", s.SkinSearch, "stm.sksearch", "0.008 0.848", "0.33 0.905",
                    40, "Buscar skin por nombre o ID y pulsa Enter...", TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(s.SkinSearch))
                Boton(c, UiSkinMain, "×", "stm.sksearch", ColClose, "0.333 0.848", "0.36 0.905", 12);

            Boton(c, UiSkinMain, modoDar ? "DAR SIN SKIN" : "QUITAR SKIN", "stm.applyskin 0", ColClose,
                  "0.37 0.848", "0.50 0.905", 12);

            var info = modoDar
                ? "Clic en una skin para recibir el item  ·  cantidad: " + (s.Amount == -1 ? "stack" : "x" + s.Amount)
                : "Clic en una skin para aplicarla al item de tu mano  ·  skin actual: " + skinActual;
            Texto(c, UiSkinMain, info, 11, TextAnchor.MiddleRight, ColDim, "0.51 0.848", "0.992 0.905");

            // ── rejilla
            Panel(c, UiSkinMain, "0 0 0 0", "0.008 0.085", "0.992 0.835", UiSkinGrid);

            if (lista.Count == 0)
            {
                Texto(c, UiSkinGrid, todas.Count == 0 ? "Este item no tiene skins aprobadas." : "Ninguna skin coincide.",
                      16, TextAnchor.MiddleCenter, ColDim, "0 0", "1 1");
            }

            for (var i = 0; i < PerPage; i++)
            {
                var idx = s.SkinPage * PerPage + i;
                if (idx >= lista.Count) break;
                var skin = lista[idx];

                string min, max;
                Celda(i, 0.004f, out min, out max);
                var cell = "stm.skcell." + i;
                var esActual = skinActual == skin.Key;

                Panel(c, UiSkinGrid, esActual ? ColGreen : ColCell, min, max, cell);
                if (esActual)
                    Panel(c, cell, ColAccent, "0 0.97", "1 1");

                c.Add(new CuiElement
                {
                    Parent = cell,
                    Components =
                    {
                        new CuiImageComponent { ItemId = def.itemid, SkinId = skin.Key },
                        new CuiRectTransformComponent { AnchorMin = "0.16 0.34", AnchorMax = "0.84 0.95" }
                    }
                });

                Texto(c, cell, skin.Value, 10, TextAnchor.MiddleCenter, ColText, "0.03 0.14", "0.97 0.35");
                Texto(c, cell, skin.Key.ToString(), 9, TextAnchor.MiddleCenter, "0.55 0.75 0.9 1", "0.02 0.02", "0.98 0.14");

                Boton(c, cell, "", "stm.applyskin " + skin.Key, "0 0 0 0", "0 0", "1 1");
            }

            // ── pie
            Texto(c, UiSkinMain, modoDar
                    ? "Clic fuera o en VOLVER para regresar al spawn de items"
                    : "Clic fuera o en CERRAR para salir   ·   /skin abre la caja del plugin Skins",
                  10, TextAnchor.MiddleLeft, ColDim, "0.01 0.015", "0.67 0.07");
            Paginacion(c, UiSkinMain, s.SkinPage, maxPage, "stm.skpage");

            return c;
        }

        // ─────────────────────────────────────────────────────────────
        //  Comandos de consola (los botones de la UI)
        // ─────────────────────────────────────────────────────────────
        [ConsoleCommand("stm.close")]
        void CcClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);
            s.MenuAbierto = false;
            s.ConfirmarLimpiarHasta = 0;
            CuiHelper.DestroyUi(p, UiRoot);
        }

        [ConsoleCommand("stm.closeskin")]
        void CcCloseSkin(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            CuiHelper.DestroyUi(p, UiSkinRoot);
        }

        [ConsoleCommand("stm.cat")]
        void CcCat(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);
            s.Category = arg.GetString(0, "@todos");
            s.Search = "";
            s.Page = 0;
            DibujarItems(p);
        }

        [ConsoleCommand("stm.page")]
        void CcPage(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            St(p).Page = arg.GetInt(0, 0);
            DibujarItems(p);
        }

        [ConsoleCommand("stm.amount")]
        void CcAmount(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var texto = arg.Args == null ? "" : string.Join("", arg.Args).Trim().TrimStart('x', 'X');
            int n;
            if (!int.TryParse(texto, out n) || (n < 1 && n != -1))
            {
                DibujarItems(p);   // entrada invalida: se redibuja con el valor anterior
                return;
            }
            St(p).Amount = Math.Min(n, MaxCantidad);
            DibujarItems(p);
        }

        [ConsoleCommand("stm.search")]
        void CcSearch(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);
            s.Search = (arg.Args == null ? "" : string.Join(" ", arg.Args)).Trim();
            s.Page = 0;
            DibujarItems(p);
        }

        [ConsoleCommand("stm.give")]
        void CcGive(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var def = ItemManager.FindItemDefinition(arg.GetInt(0, 0));
            if (def == null) return;

            var s = St(p);
            var dado = DarItem(p, def, CantidadPara(s, def), 0UL);
            AnadirReciente(s, def.itemid);
            p.ChatMessage("<color=#8cf>+</color> " + NombreDe(def) + " x" + dado);
        }

        // Dos clics: el primero arma el boton ("¿SEGURO?") durante unos segundos
        [ConsoleCommand("stm.clearinv")]
        void CcClearInv(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);

            if (Time.realtimeSinceStartup < s.ConfirmarLimpiarHasta)
            {
                s.ConfirmarLimpiarHasta = 0;
                LimpiarInventario(p);
                DibujarItems(p);
                return;
            }

            s.ConfirmarLimpiarHasta = Time.realtimeSinceStartup + SegundosConfirmar;
            DibujarItems(p);

            // Si no confirma, el boton vuelve a su estado normal
            timer.Once(SegundosConfirmar + 0.1f, () =>
            {
                if (p == null || !p.IsConnected || !s.MenuAbierto || s.ConfirmarLimpiarHasta == 0) return;
                s.ConfirmarLimpiarHasta = 0;
                DibujarItems(p);
            });
        }

        [ConsoleCommand("stm.skinsof")]
        void CcSkinsOf(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var def = ItemManager.FindItemDefinition(arg.GetInt(0, 0));
            if (def == null) return;

            var s = St(p);
            s.SkinDarItemId = def.itemid;
            s.UltimaSkinDada = 0;
            s.SkinPage = 0;
            s.SkinSearch = "";
            DibujarSkins(p);
        }

        [ConsoleCommand("stm.skpage")]
        void CcSkPage(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            St(p).SkinPage = arg.GetInt(0, 0);
            DibujarSkins(p);
        }

        [ConsoleCommand("stm.sksearch")]
        void CcSkSearch(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);
            s.SkinSearch = (arg.Args == null ? "" : string.Join(" ", arg.Args)).Trim();
            s.SkinPage = 0;
            DibujarSkins(p);
        }

        [ConsoleCommand("stm.applyskin")]
        void CcApplySkin(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);

            ulong skinId;
            if (!ulong.TryParse(arg.GetString(0, "0"), out skinId)) return;

            // Modo "ELEGIR SKIN" del /menu: dar el item ya con la skin
            if (s.SkinDarItemId != 0)
            {
                var def = ItemManager.FindItemDefinition(s.SkinDarItemId);
                if (def == null) return;

                var dado = DarItem(p, def, CantidadPara(s, def), skinId);
                AnadirReciente(s, def.itemid);
                s.UltimaSkinDada = skinId;
                p.ChatMessage("<color=#8cf>+</color> " + NombreDe(def) + " x" + dado
                              + (skinId == 0 ? " (sin skin)" : " <color=#9ab>skin " + skinId + "</color>"));
                DibujarSkins(p);
                return;
            }

            var item = ItemEnMano(p);
            if (item == null)
            {
                p.ChatMessage("<color=#e88>Coge un item en la mano primero.</color>");
                return;
            }

            item.skin = skinId;
            item.MarkDirty();

            var held = item.GetHeldEntity();
            if (held != null)
            {
                held.skinID = skinId;
                held.SendNetworkUpdate();
            }

            p.ChatMessage(skinId == 0
                ? "<color=#8cf>Skin quitada.</color>"
                : "<color=#8cf>Skin aplicada:</color> " + skinId);

            DibujarSkins(p);
        }

        // ─────────────────────────────────────────────────────────────
        //  Autotest: construye las UIs y las serializa sin jugador,
        //  para validar la estructura desde la consola del servidor.
        // ─────────────────────────────────────────────────────────────
        [ConsoleCommand("stm.selftest")]
        void CcSelfTest(ConsoleSystem.Arg arg)
        {
            var fallos = 0;
            var vistas = 0;
            var s = new State();
            s.Recientes.AddRange(ItemManager.itemList.Take(5).Select(i => i.itemid));

            // menu de items: todas las pestanas + busqueda + cantidad personalizada + confirmar limpiar
            var pestanas = new List<string> { "@todos", "@recientes", "@skins" };
            pestanas.AddRange(_categories);
            foreach (var tab in pestanas)
            {
                s.Category = tab;
                s.Search = "";
                s.Page = 0;
                try
                {
                    var json = CuiHelper.ToJson(ConstruirItems(s));
                    if (string.IsNullOrEmpty(json) || json.Length < 100)
                        { Puts("FALLO items[" + tab + "]: json vacio"); fallos++; }
                    vistas++;
                }
                catch (Exception e) { Puts("FALLO items[" + tab + "]: " + e.Message); fallos++; }
            }

            s.Category = "@todos"; s.Search = "rifle"; s.Page = 0; s.Amount = 250;
            s.ConfirmarLimpiarHasta = Time.realtimeSinceStartup + 10f;
            try
            {
                var json = CuiHelper.ToJson(ConstruirItems(s));
                if (json.Contains(",5 ") || json.Contains("\"0,")) { Puts("FALLO: coordenadas con coma decimal"); fallos++; }
                vistas++;
            }
            catch (Exception e) { Puts("FALLO busqueda: " + e.Message); fallos++; }

            s.Search = "zzzz_no_existe";
            try { CuiHelper.ToJson(ConstruirItems(s)); vistas++; }
            catch (Exception e) { Puts("FALLO busqueda vacia: " + e.Message); fallos++; }

            // menu de skins: un item con muchas skins, uno sin ninguna, en los dos modos
            var conSkins = ItemManager.FindItemDefinition("rifle.ak");
            var sinSkins = ItemManager.itemList.FirstOrDefault(i => !_skinsByItem.ContainsKey(i.shortname));

            foreach (var def in new[] { conSkins, sinSkins })
            {
                if (def == null) continue;
                foreach (var dar in new[] { 0, def.itemid })
                {
                    var st = new State { SkinDarItemId = dar };
                    try
                    {
                        var json = CuiHelper.ToJson(ConstruirSkins(def, 0UL, st));
                        if (string.IsNullOrEmpty(json)) { Puts("FALLO skins[" + def.shortname + "]"); fallos++; }
                        vistas++;
                    }
                    catch (Exception e) { Puts("FALLO skins[" + def.shortname + "]: " + e.Message); fallos++; }
                }
            }

            // paginacion al final de la lista mas larga
            if (conSkins != null)
            {
                var st = new State { SkinPage = 999 };
                try { CuiHelper.ToJson(ConstruirSkins(conSkins, 0UL, st)); vistas++; }
                catch (Exception e) { Puts("FALLO paginacion skins: " + e.Message); fallos++; }
            }

            Puts(fallos == 0
                ? "SELFTEST OK - " + vistas + " vistas construidas sin errores"
                : "SELFTEST con " + fallos + " fallos");
        }

        // ─────────────────────────────────────────────────────────────
        //  Exportar la documentacion en Markdown
        // ─────────────────────────────────────────────────────────────
        [ConsoleCommand("skintest.exportmd")]
        void CcExport(ConsoleSystem.Arg arg)
        {
            var jugador = arg.Player();
            if (jugador != null && !jugador.IsAdmin) return;

            // oxide/ -> server/ -> raiz del repo
            var dir = Path.GetFullPath(Path.Combine(Interface.Oxide.RootDirectory, "..", "docs"));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var rutaSkins = Path.Combine(dir, "SKINS.md");
            var rutaItems = Path.Combine(dir, "ITEMS.md");

            EscribirSkinsMd(rutaSkins);
            EscribirItemsMd(rutaItems);

            Puts("Exportado: " + rutaSkins + " | " + rutaItems);
            if (jugador != null) jugador.ChatMessage("<color=#8cf>Markdown exportado a docs/</color>");
        }

        void EscribirSkinsMd(string ruta)
        {
            var sb = new StringBuilder();
            var total = _skinsByItem.Values.Sum(l => l.Count);

            sb.AppendLine("# 🎨 Skins de Rust — lista completa con IDs");
            sb.AppendLine();
            sb.AppendLine("> Generado automaticamente desde `Rust.Workshop.Approved.All` de esta build del servidor.");
            sb.AppendLine("> **" + total + " skins** repartidas en **" + _skinsByItem.Count + " items**.");
            sb.AppendLine(">");
            sb.AppendLine("> Para regenerarlo: en la consola del servidor (F1) ejecuta `skintest.exportmd`.");
            sb.AppendLine();
            sb.AppendLine("## 🚀 Como usar un ID");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine("inventory.give <shortname> 1 1 <skinID>   # te da el item ya con la skin puesta");
            sb.AppendLine("global.skin_looking <skinID>              # aplica la skin a lo que estas mirando");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("O simplemente `/sk` en el chat con el item en la mano. 🖱️");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            var ordenados = _skinsByItem.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToList();

            sb.AppendLine("## 📑 Indice");
            sb.AppendLine();
            foreach (var kv in ordenados)
            {
                var nombre = NombreDe(ItemManager.FindItemDefinition(kv.Key));
                sb.AppendLine("- [" + nombre + " (`" + kv.Key + "`)](#" + Ancla(nombre, kv.Key) + ") — " + kv.Value.Count + " skins");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var kv in ordenados)
            {
                var nombre = NombreDe(ItemManager.FindItemDefinition(kv.Key));

                sb.AppendLine("## " + nombre + " (`" + kv.Key + "`)");
                sb.AppendLine();
                sb.AppendLine("`" + kv.Value.Count + " skins`");
                sb.AppendLine();
                sb.AppendLine("| Skin | ID |");
                sb.AppendLine("|---|---|");
                foreach (var s in kv.Value)
                    sb.AppendLine("| " + s.Value.Replace("|", "\\|") + " | `" + s.Key + "` |");
                sb.AppendLine();
            }

            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        }

        static string Ancla(string nombre, string shortname)
        {
            var raw = (nombre + " " + shortname).ToLower();
            var sb = new StringBuilder();
            foreach (var ch in raw)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch == ' ' || ch == '-' || ch == '.' || ch == '_') sb.Append('-');
            }
            return sb.ToString();
        }

        void EscribirItemsMd(string ruta)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 📦 Items de Rust — lista completa");
            sb.AppendLine();
            sb.AppendLine("> **" + ItemManager.itemList.Count + " items** en **" + _categories.Count + " categorias**.");
            sb.AppendLine("> Generado con `skintest.exportmd`.");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine("inventory.give <shortname> <cantidad>");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("O usa `/menu` en el chat para el buscador visual. 🖱️");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var cat in _categories.OrderBy(k => k))
            {
                var items = ItemManager.itemList
                    .Where(i => i.category.ToString() == cat)
                    .OrderBy(i => i.shortname)
                    .ToList();

                sb.AppendLine("## " + cat + " (" + items.Count + ")");
                sb.AppendLine();
                sb.AppendLine("| Nombre | shortname | itemid | Skins |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var i in items)
                {
                    var n = _skinsByItem.ContainsKey(i.shortname) ? _skinsByItem[i.shortname].Count.ToString() : "-";
                    sb.AppendLine("| " + NombreDe(i).Replace("|", "\\|") + " | `" + i.shortname + "` | `" + i.itemid + "` | " + n + " |");
                }
                sb.AppendLine();
            }

            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
