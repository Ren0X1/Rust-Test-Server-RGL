using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SkinTestMenu", "local", "1.0.0")]
    [Description("Menu de spawn de items por categorias y menu de skins del item en la mano")]
    public class SkinTestMenu : RustPlugin
    {
        // ─────────────────────────────────────────────────────────────
        //  Constantes de UI
        // ─────────────────────────────────────────────────────────────
        const string UiRoot = "stm.root";
        const string UiGrid = "stm.grid";
        const string UiSkinRoot = "stm.skinroot";
        const string UiSkinGrid = "stm.skingrid";

        const int Cols = 7;
        const int Rows = 4;
        const int PerPage = Cols * Rows;   // 28

        const string ColBack = "0.13 0.13 0.15 0.98";
        const string ColPanel = "0.18 0.18 0.21 0.95";
        const string ColCell = "0.22 0.22 0.26 0.90";
        const string ColBtn = "0.30 0.42 0.55 0.95";
        const string ColBtnOn = "0.35 0.62 0.42 1.00";
        const string ColClose = "0.65 0.25 0.25 0.95";
        const string ColText = "0.90 0.90 0.92 1.00";
        const string ColDim = "0.60 0.60 0.65 1.00";

        // ─────────────────────────────────────────────────────────────
        //  Estado por jugador
        // ─────────────────────────────────────────────────────────────
        class State
        {
            public string Category = "@todos";
            public int Page;
            public string Search = "";
            public int Amount = 1;
            public int SkinPage;
            public string SkinSearch = "";
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
            _categories = ItemManager.itemList
                .Select(i => i.category.ToString())
                .Distinct()
                .OrderBy(c => c)
                .ToList();
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
        //  MENU DE ITEMS
        // ─────────────────────────────────────────────────────────────
        List<ItemDefinition> FiltrarItems(State s)
        {
            IEnumerable<ItemDefinition> q = ItemManager.itemList;

            if (!string.IsNullOrEmpty(s.Search))
            {
                var needle = s.Search.ToLower();
                q = q.Where(i => i.shortname.ToLower().Contains(needle)
                              || NombreDe(i).ToLower().Contains(needle));
            }
            else if (s.Category == "@skins")
            {
                q = q.Where(i => _skinsByItem.ContainsKey(i.shortname));
            }
            else if (s.Category != "@todos")
            {
                q = q.Where(i => i.category.ToString() == s.Category);
            }

            return q.OrderBy(i => NombreDe(i)).ToList();
        }

        void DibujarItems(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiRoot);
            CuiHelper.AddUi(player, ConstruirItems(St(player)));
        }

        CuiElementContainer ConstruirItems(State s)
        {
            var items = FiltrarItems(s);
            var maxPage = Math.Max(0, (items.Count - 1) / PerPage);
            if (s.Page > maxPage) s.Page = maxPage;
            if (s.Page < 0) s.Page = 0;

            var c = new CuiElementContainer();

            c.Add(new CuiPanel
            {
                Image = { Color = ColBack },
                RectTransform = { AnchorMin = "0.06 0.08", AnchorMax = "0.94 0.94" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            c.Add(new CuiLabel
            {
                Text = { Text = "SPAWN DE ITEMS", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = ColText },
                RectTransform = { AnchorMin = "0.015 0.985", AnchorMax = "0.4 1.03" }
            }, UiRoot);

            c.Add(new CuiLabel
            {
                Text = { Text = items.Count + " items  -  pagina " + (s.Page + 1) + "/" + (maxPage + 1),
                         FontSize = 12, Align = TextAnchor.MiddleRight, Color = ColDim },
                RectTransform = { AnchorMin = "0.55 0.985", AnchorMax = "0.86 1.03" }
            }, UiRoot);

            c.Add(new CuiButton
            {
                Button = { Color = ColClose, Command = "stm.close" },
                Text = { Text = "CERRAR", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = "0.885 0.982", AnchorMax = "0.985 1.032" }
            }, UiRoot);

            // ── barra lateral de categorias
            c.Add(new CuiPanel
            {
                Image = { Color = ColPanel },
                RectTransform = { AnchorMin = "0.008 0.01", AnchorMax = "0.145 0.975" }
            }, UiRoot, "stm.side");

            var tabs = new List<string> { "@todos", "@skins" };
            tabs.AddRange(_categories);

            var alto = 1f / Math.Max(tabs.Count, 1);
            for (var i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                var etiqueta = tab == "@todos" ? "TODOS" : tab == "@skins" ? "CON SKINS" : tab.ToUpper();
                var activo = string.IsNullOrEmpty(s.Search) && s.Category == tab;
                var y1 = 1f - (i + 1) * alto;
                var y2 = 1f - i * alto;

                c.Add(new CuiButton
                {
                    Button = { Color = activo ? ColBtnOn : ColCell, Command = "stm.cat " + tab },
                    Text = { Text = etiqueta, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform =
                    {
                        AnchorMin = "0.04 " + (y1 + 0.004f).ToString("0.####"),
                        AnchorMax = "0.96 " + (y2 - 0.004f).ToString("0.####")
                    }
                }, "stm.side");
            }

            // ── buscador
            c.Add(new CuiPanel
            {
                Image = { Color = ColCell },
                RectTransform = { AnchorMin = "0.155 0.925", AnchorMax = "0.52 0.975" }
            }, UiRoot, "stm.searchbox");

            c.Add(new CuiElement
            {
                Parent = "stm.searchbox",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = s.Search, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = ColText,
                        CharsLimit = 40, Command = "stm.search", NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
                }
            });

            if (string.IsNullOrEmpty(s.Search))
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = "escribe y pulsa Enter para buscar...", FontSize = 11,
                             Align = TextAnchor.MiddleLeft, Color = "0.5 0.5 0.55 1" },
                    RectTransform = { AnchorMin = "0.17 0.925", AnchorMax = "0.52 0.975" }
                }, UiRoot);
            }
            else
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColClose, Command = "stm.cat @todos" },
                    Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.527 0.925", AnchorMax = "0.558 0.975" }
                }, UiRoot);
            }

            // ── selector de cantidad
            var cantidades = new[] { 1, 10, 100, 1000 };
            for (var i = 0; i < cantidades.Length; i++)
            {
                var n = cantidades[i];
                var x1 = 0.60f + i * 0.075f;
                c.Add(new CuiButton
                {
                    Button = { Color = s.Amount == n ? ColBtnOn : ColCell, Command = "stm.amount " + n },
                    Text = { Text = "x" + n, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform =
                    {
                        AnchorMin = x1.ToString("0.####") + " 0.925",
                        AnchorMax = (x1 + 0.068f).ToString("0.####") + " 0.975"
                    }
                }, UiRoot);
            }

            c.Add(new CuiButton
            {
                Button = { Color = s.Amount == -1 ? ColBtnOn : ColCell, Command = "stm.amount -1" },
                Text = { Text = "STACK", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = "0.90 0.925", AnchorMax = "0.99 0.975" }
            }, UiRoot);

            // ── rejilla
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.155 0.075", AnchorMax = "0.99 0.915" }
            }, UiRoot, UiGrid);

            var anchoCelda = 1f / Cols;
            var altoCelda = 1f / Rows;

            for (var i = 0; i < PerPage; i++)
            {
                var idx = s.Page * PerPage + i;
                if (idx >= items.Count) break;
                var def = items[idx];

                var col = i % Cols;
                var fila = i / Cols;
                var x1 = col * anchoCelda;
                var y2 = 1f - fila * altoCelda;
                var y1 = y2 - altoCelda;

                var cell = "stm.cell." + i;
                c.Add(new CuiPanel
                {
                    Image = { Color = ColCell },
                    RectTransform =
                    {
                        AnchorMin = (x1 + 0.005f).ToString("0.####") + " " + (y1 + 0.012f).ToString("0.####"),
                        AnchorMax = (x1 + anchoCelda - 0.005f).ToString("0.####") + " " + (y2 - 0.012f).ToString("0.####")
                    }
                }, UiGrid, cell);

                c.Add(new CuiElement
                {
                    Parent = cell,
                    Components =
                    {
                        new CuiImageComponent { ItemId = def.itemid },
                        new CuiRectTransformComponent { AnchorMin = "0.18 0.32", AnchorMax = "0.82 0.94" }
                    }
                });

                c.Add(new CuiLabel
                {
                    Text = { Text = NombreDe(def), FontSize = 10, Align = TextAnchor.UpperCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.02 0.10", AnchorMax = "0.98 0.34" }
                }, cell);

                var nSkins = _skinsByItem.ContainsKey(def.shortname) ? _skinsByItem[def.shortname].Count : 0;
                c.Add(new CuiLabel
                {
                    Text = { Text = nSkins > 0 ? nSkins + " skins" : def.shortname,
                             FontSize = 9, Align = TextAnchor.LowerCenter,
                             Color = nSkins > 0 ? "0.45 0.75 0.5 1" : ColDim },
                    RectTransform = { AnchorMin = "0.02 0.01", AnchorMax = "0.98 0.13" }
                }, cell);

                c.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = "stm.give " + def.itemid },
                    Text = { Text = "" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, cell);
            }

            if (s.Page > 0)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColBtn, Command = "stm.page " + (s.Page - 1) },
                    Text = { Text = "< ANTERIOR", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.155 0.015", AnchorMax = "0.30 0.065" }
                }, UiRoot);
            }

            if (s.Page < maxPage)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColBtn, Command = "stm.page " + (s.Page + 1) },
                    Text = { Text = "SIGUIENTE >", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.845 0.015", AnchorMax = "0.99 0.065" }
                }, UiRoot);
            }

            c.Add(new CuiLabel
            {
                Text = { Text = "Clic en un item para recibirlo  -  /sk abre las skins del item que lleves en la mano",
                         FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColDim },
                RectTransform = { AnchorMin = "0.31 0.015", AnchorMax = "0.84 0.065" }
            }, UiRoot);

            return c;
        }

        // ─────────────────────────────────────────────────────────────
        //  MENU DE SKINS
        // ─────────────────────────────────────────────────────────────
        void DibujarSkins(BasePlayer player)
        {
            var item = ItemEnMano(player);
            if (item == null)
            {
                CuiHelper.DestroyUi(player, UiSkinRoot);
                player.ChatMessage("<color=#e88>Coge un item en la mano primero.</color>");
                return;
            }

            CuiHelper.DestroyUi(player, UiSkinRoot);
            CuiHelper.AddUi(player, ConstruirSkins(item.info, item.skin, St(player)));
        }

        CuiElementContainer ConstruirSkins(ItemDefinition def, ulong skinActual, State s)
        {
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

            c.Add(new CuiPanel
            {
                Image = { Color = ColBack },
                RectTransform = { AnchorMin = "0.06 0.08", AnchorMax = "0.94 0.94" },
                CursorEnabled = true
            }, "Overlay", UiSkinRoot);

            c.Add(new CuiLabel
            {
                Text = { Text = "SKINS DE: " + NombreDe(def).ToUpper(), FontSize = 20,
                         Align = TextAnchor.MiddleLeft, Color = ColText },
                RectTransform = { AnchorMin = "0.015 0.985", AnchorMax = "0.55 1.03" }
            }, UiSkinRoot);

            c.Add(new CuiLabel
            {
                Text = { Text = lista.Count + " skins  -  pagina " + (s.SkinPage + 1) + "/" + (maxPage + 1),
                         FontSize = 12, Align = TextAnchor.MiddleRight, Color = ColDim },
                RectTransform = { AnchorMin = "0.55 0.985", AnchorMax = "0.86 1.03" }
            }, UiSkinRoot);

            c.Add(new CuiButton
            {
                Button = { Color = ColClose, Command = "stm.closeskin" },
                Text = { Text = "CERRAR", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = "0.885 0.982", AnchorMax = "0.985 1.032" }
            }, UiSkinRoot);

            c.Add(new CuiPanel
            {
                Image = { Color = ColCell },
                RectTransform = { AnchorMin = "0.008 0.925", AnchorMax = "0.35 0.975" }
            }, UiSkinRoot, "stm.sksearchbox");

            c.Add(new CuiElement
            {
                Parent = "stm.sksearchbox",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = s.SkinSearch, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = ColText,
                        CharsLimit = 40, Command = "stm.sksearch", NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
                }
            });

            if (string.IsNullOrEmpty(s.SkinSearch))
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = "buscar skin por nombre o ID...", FontSize = 11,
                             Align = TextAnchor.MiddleLeft, Color = "0.5 0.5 0.55 1" },
                    RectTransform = { AnchorMin = "0.025 0.925", AnchorMax = "0.35 0.975" }
                }, UiSkinRoot);
            }

            c.Add(new CuiButton
            {
                Button = { Color = ColClose, Command = "stm.applyskin 0" },
                Text = { Text = "QUITAR SKIN (por defecto)", FontSize = 12,
                         Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = "0.36 0.925", AnchorMax = "0.60 0.975" }
            }, UiSkinRoot);

            c.Add(new CuiLabel
            {
                Text = { Text = "skin actual: " + skinActual, FontSize = 12,
                         Align = TextAnchor.MiddleRight, Color = ColDim },
                RectTransform = { AnchorMin = "0.61 0.925", AnchorMax = "0.99 0.975" }
            }, UiSkinRoot);

            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.008 0.075", AnchorMax = "0.99 0.915" }
            }, UiSkinRoot, UiSkinGrid);

            if (lista.Count == 0)
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = "Este item no tiene skins aprobadas.", FontSize = 16,
                             Align = TextAnchor.MiddleCenter, Color = ColDim },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, UiSkinGrid);
            }

            var anchoCelda = 1f / Cols;
            var altoCelda = 1f / Rows;

            for (var i = 0; i < PerPage; i++)
            {
                var idx = s.SkinPage * PerPage + i;
                if (idx >= lista.Count) break;
                var skin = lista[idx];

                var col = i % Cols;
                var fila = i / Cols;
                var x1 = col * anchoCelda;
                var y2 = 1f - fila * altoCelda;
                var y1 = y2 - altoCelda;

                var cell = "stm.skcell." + i;
                var esActual = skinActual == skin.Key;

                c.Add(new CuiPanel
                {
                    Image = { Color = esActual ? ColBtnOn : ColCell },
                    RectTransform =
                    {
                        AnchorMin = (x1 + 0.004f).ToString("0.####") + " " + (y1 + 0.012f).ToString("0.####"),
                        AnchorMax = (x1 + anchoCelda - 0.004f).ToString("0.####") + " " + (y2 - 0.012f).ToString("0.####")
                    }
                }, UiSkinGrid, cell);

                c.Add(new CuiElement
                {
                    Parent = cell,
                    Components =
                    {
                        new CuiImageComponent { ItemId = def.itemid, SkinId = skin.Key },
                        new CuiRectTransformComponent { AnchorMin = "0.16 0.34", AnchorMax = "0.84 0.95" }
                    }
                });

                c.Add(new CuiLabel
                {
                    Text = { Text = skin.Value, FontSize = 10, Align = TextAnchor.UpperCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.02 0.12", AnchorMax = "0.98 0.36" }
                }, cell);

                c.Add(new CuiLabel
                {
                    Text = { Text = skin.Key.ToString(), FontSize = 10,
                             Align = TextAnchor.LowerCenter, Color = "0.55 0.75 0.9 1" },
                    RectTransform = { AnchorMin = "0.02 0.01", AnchorMax = "0.98 0.14" }
                }, cell);

                c.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = "stm.applyskin " + skin.Key },
                    Text = { Text = "" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, cell);
            }

            if (s.SkinPage > 0)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColBtn, Command = "stm.skpage " + (s.SkinPage - 1) },
                    Text = { Text = "< ANTERIOR", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.008 0.015", AnchorMax = "0.15 0.065" }
                }, UiSkinRoot);
            }

            if (s.SkinPage < maxPage)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColBtn, Command = "stm.skpage " + (s.SkinPage + 1) },
                    Text = { Text = "SIGUIENTE >", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.845 0.015", AnchorMax = "0.99 0.065" }
                }, UiSkinRoot);
            }

            c.Add(new CuiLabel
            {
                Text = { Text = "Clic en una skin para aplicarla al item que llevas en la mano",
                         FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColDim },
                RectTransform = { AnchorMin = "0.16 0.015", AnchorMax = "0.84 0.065" }
            }, UiSkinRoot);

            return c;
        }

        // ─────────────────────────────────────────────────────────────
        //  Comandos de consola (los botones de la UI)
        // ─────────────────────────────────────────────────────────────
        [ConsoleCommand("stm.close")]
        void CcClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
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
            St(p).Amount = arg.GetInt(0, 1);
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
            var cantidad = s.Amount == -1 ? Math.Max(1, def.stackable) : s.Amount;

            var item = ItemManager.Create(def, cantidad);
            if (item == null) return;

            p.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            p.ChatMessage("<color=#8cf>+</color> " + NombreDe(def) + " x" + cantidad);
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

            var item = ItemEnMano(p);
            if (item == null)
            {
                p.ChatMessage("<color=#e88>Coge un item en la mano primero.</color>");
                return;
            }

            ulong skinId;
            if (!ulong.TryParse(arg.GetString(0, "0"), out skinId)) return;

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
        //  Autotest: construye las dos UIs y las serializa sin jugador,
        //  para validar la estructura desde la consola del servidor.
        // ─────────────────────────────────────────────────────────────
        [ConsoleCommand("stm.selftest")]
        void CcSelfTest(ConsoleSystem.Arg arg)
        {
            var fallos = 0;
            var s = new State();

            // menu de items: todas las pestanas + busqueda
            var pestanas = new List<string> { "@todos", "@skins" };
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
                }
                catch (Exception e) { Puts("FALLO items[" + tab + "]: " + e.Message); fallos++; }
            }

            s.Category = "@todos"; s.Search = "rifle"; s.Page = 0;
            try { CuiHelper.ToJson(ConstruirItems(s)); }
            catch (Exception e) { Puts("FALLO busqueda: " + e.Message); fallos++; }

            // menu de skins: un item con muchas skins, uno sin ninguna
            var conSkins = ItemManager.FindItemDefinition("rifle.ak");
            var sinSkins = ItemManager.itemList.FirstOrDefault(i => !_skinsByItem.ContainsKey(i.shortname));

            foreach (var def in new[] { conSkins, sinSkins })
            {
                if (def == null) continue;
                var st = new State();
                try
                {
                    var json = CuiHelper.ToJson(ConstruirSkins(def, 0UL, st));
                    if (string.IsNullOrEmpty(json)) { Puts("FALLO skins[" + def.shortname + "]"); fallos++; }
                }
                catch (Exception e) { Puts("FALLO skins[" + def.shortname + "]: " + e.Message); fallos++; }
            }

            // paginacion al final de la lista mas larga
            if (conSkins != null)
            {
                var st = new State { SkinPage = 999 };
                try { CuiHelper.ToJson(ConstruirSkins(conSkins, 0UL, st)); }
                catch (Exception e) { Puts("FALLO paginacion skins: " + e.Message); fallos++; }
            }

            Puts(fallos == 0
                ? "SELFTEST OK - " + (pestanas.Count + 4) + " vistas construidas sin errores"
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

            foreach (var cat in _categories)
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
