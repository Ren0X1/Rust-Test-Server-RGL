using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("GuiaRust", "local", "1.2.0")]
    [Description("Guia en el juego (/help): monumentos del mapa, eventos, puzzles, metro, labs y vehiculos")]
    public class GuiaRust : RustPlugin
    {
        // ─────────────────────────────────────────────────────────────
        //  UI
        // ─────────────────────────────────────────────────────────────
        const string UiRoot = "guia.root";
        const string UiMain = "guia.main";
        const string UiGrid = "guia.grid";

        const int Cols = 2;
        const int Rows = 3;
        const int PerPage = Cols * Rows;

        const string ColBack = "0.11 0.115 0.13 0.98";
        const string ColHeader = "0.075 0.08 0.095 1";
        const string ColAccent = "0.85 0.45 0.18 1";
        const string ColPanel = "0.15 0.155 0.18 0.95";
        const string ColCell = "0.20 0.205 0.235 0.92";
        const string ColBtn = "0.26 0.28 0.32 0.95";
        const string ColBtnOn = "0.80 0.42 0.16 0.95";
        const string ColGreen = "0.28 0.52 0.30 0.95";
        const string ColClose = "0.60 0.22 0.20 0.95";
        const string ColText = "0.92 0.92 0.94 1";
        const string ColDim = "0.62 0.62 0.67 1";
        const string ColSoft = "0.45 0.45 0.50 1";

        class Entrada
        {
            public string Titulo;
            public string Etiqueta;      // tier, safezone, distancia...
            public string Texto;
            public Vector3 Pos;          // Vector3.zero = sin teletransporte
        }

        readonly Dictionary<ulong, int[]> _estado = new Dictionary<ulong, int[]>();   // [seccion, pagina]

        int[] St(BasePlayer p)
        {
            int[] s;
            if (!_estado.TryGetValue(p.userID, out s)) _estado[p.userID] = s = new int[] { 0, 0 };
            return s;
        }

        static readonly string[] Secciones = { "MONUMENTOS", "EVENTOS", "PUZZLES Y TARJETAS", "METRO Y LABS", "VEHICULOS", "COMANDOS" };

        void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, UiRoot);
        }

        // ─────────────────────────────────────────────────────────────
        //  Monumentos: se leen del mapa cargado, no de una lista fija,
        //  asi la guia siempre corresponde al mapa que esta puesto.
        // ─────────────────────────────────────────────────────────────
        static readonly List<KeyValuePair<string, string>> Descripciones = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("launch site", "El monumento mas grande y peligroso. Puzzle de <color=#f77>tarjeta roja</color>, recicladores, cajas de elite y mucha radiacion. Por fuera patrulla el <color=#fb7>Bradley APC</color> (aqui esta desactivado: bradley.enabled)."),
            new KeyValuePair<string, string>("airfield", "Hangares, torre de control y tuneles. Puzzle de <color=#7af>tarjeta azul</color>, reciclador y bastantes cajas. Radiacion baja."),
            new KeyValuePair<string, string>("water treatment", "Muy largo y abierto, lleno de barriles y cajas. Puzzle de <color=#7af>tarjeta azul</color> y reciclador."),
            new KeyValuePair<string, string>("train yard", "Estacion de tren con la torre roja. Puzzle de <color=#7af>tarjeta azul</color>, reciclador y salida al metro en algunos mapas."),
            new KeyValuePair<string, string>("power plant", "Central electrica con la torre de refrigeracion. Puzzle de <color=#7af>tarjeta azul</color>, reciclador y cajas buenas."),
            new KeyValuePair<string, string>("military tunnel", "Tunel militar: los cientificos mas duros del mapa. Puzzle de <color=#7f7>tarjeta verde</color> con fusibles y sala de recompensa."),
            new KeyValuePair<string, string>("satellite", "Antena parabolica gigante. Puzzle de <color=#7f7>tarjeta verde</color>, reciclador y muchos barriles alrededor."),
            new KeyValuePair<string, string>("sphere", "La bola gigante (Dome). Se sube por las rampas de fuera; cajas y barriles por las plataformas."),
            new KeyValuePair<string, string>("harbor", "Puerto con gruas y contenedores. Reciclador y cajas. Aqui es donde atraca el <color=#fb7>Cargo Ship</color>."),
            new KeyValuePair<string, string>("junkyard", "Desguace con el iman gigante. Muchos barriles, cajas y coches para reciclar."),
            new KeyValuePair<string, string>("sewer", "Ramal de alcantarillado. Puzzle de <color=#7f7>tarjeta verde</color> y reciclador; compacto y facil de limpiar."),
            new KeyValuePair<string, string>("outpost", "<color=#7f7>Zona segura</color>: no se puede disparar. Tiendas, reciclador, mesa de investigacion, banco de reparacion y taller. El sitio para vender chatarra."),
            new KeyValuePair<string, string>("bandit", "<color=#7f7>Zona segura</color>: casino, vendedores, reciclador y el helipuerto donde se compra el scrap heli."),
            new KeyValuePair<string, string>("large oil rig", "Plataforma grande en el mar: cientificos, cajas bloqueadas que hay que hackear y mucho loot de alto nivel. Se llega en barco o volando."),
            new KeyValuePair<string, string>("oil rig", "Plataforma pequena en el mar: cientificos y cajas bloqueadas. Version reducida de la grande."),
            new KeyValuePair<string, string>("excavator", "Excavadora gigante: se enciende con diesel y produce piedra, metal, azufre o HQM sin parar. Mira <color=#ff0>/help</color> &gt; EVENTOS para los pasos."),
            new KeyValuePair<string, string>("fishing", "<color=#7f7>Zona segura</color>: se compran barcos, submarinos y equipo de pesca. Tiene reciclador."),
            new KeyValuePair<string, string>("ranch", "Rancho: caballos y algo de loot suelto."),
            new KeyValuePair<string, string>("barn", "Establo: caballos y loot ligero."),
            new KeyValuePair<string, string>("supermarket", "Monumento pequeno de carretera: cajas, barriles y un vendedor."),
            new KeyValuePair<string, string>("gas station", "Monumento pequeno de carretera: barriles, cajas y reciclador en algunos casos."),
            new KeyValuePair<string, string>("warehouse", "Nave pequena con barriles y cajas. Buen sitio para empezar."),
            new KeyValuePair<string, string>("lighthouse", "Faro en la costa: barriles, cajas y vistas. Se sube hasta arriba."),
            new KeyValuePair<string, string>("mining", "Puesto minero: reciclador, hornos y algo de loot. Suele estar en las carreteras."),
            new KeyValuePair<string, string>("quarry", "Cantera: se enciende con combustible y da recursos segun el tipo (piedra, azufre o HQM)."),
            new KeyValuePair<string, string>("arctic", "Base de investigacion artica: puzzle, loot bueno y motos de nieve. Hace frio: abrigate."),
            new KeyValuePair<string, string>("missile silo", "Silo de misiles: puzzle largo y loot de los mejores del mapa."),
            new KeyValuePair<string, string>("ferry", "Terminal de ferry: monumento de costa con cajas y barriles."),
            new KeyValuePair<string, string>("radtown", "Radtown: monumento con <color=#f77>radiacion</color>, barriles y cajas por todas las torres."),
            new KeyValuePair<string, string>("train tunnel", "Entrada al <color=#7af>metro</color>: se baja por las escaleras o el ascensor a la red de tuneles subterraneos."),
            new KeyValuePair<string, string>("underwater", "Laboratorio submarino: se entra con submarino por las piscinas interiores. Puzzles, cientificos y loot."),
            new KeyValuePair<string, string>("cave", "Cueva: barriles, cajas y buen sitio para construir una base escondida."),
            new KeyValuePair<string, string>("swamp", "Zona pantanosa con loot suelto y algun vendedor."),
            new KeyValuePair<string, string>("junkpile", "Monton de chatarra: barriles y a veces cientificos. Aparece y desaparece solo."),
        };

        static string DescripcionDe(string nombre)
        {
            var n = (nombre ?? "").ToLower();
            foreach (var kv in Descripciones) if (n.Contains(kv.Key)) return kv.Value;
            return "Monumento del mapa. Mira los barriles, las cajas y si tiene reciclador.";
        }

        List<Entrada> Monumentos(BasePlayer player)
        {
            var salida = new List<Entrada>();
            var monumentos = TerrainMeta.Path != null ? TerrainMeta.Path.Monuments : null;
            if (monumentos == null) return salida;

            // Los repetidos (cuevas, junkpiles...) se agrupan y se muestra el mas cercano
            var grupos = new Dictionary<string, List<MonumentInfo>>();
            foreach (var m in monumentos)
            {
                if (m == null) continue;
                var nombre = m.displayPhrase != null && !string.IsNullOrEmpty(m.displayPhrase.english)
                    ? m.displayPhrase.english
                    : m.name;
                if (string.IsNullOrEmpty(nombre)) continue;
                List<MonumentInfo> lista;
                if (!grupos.TryGetValue(nombre, out lista)) grupos[nombre] = lista = new List<MonumentInfo>();
                lista.Add(m);
            }

            var pos = player.transform.position;
            foreach (var kv in grupos)
            {
                var cercano = kv.Value.OrderBy(m => Vector3.Distance(pos, m.transform.position)).First();
                var dist = Mathf.RoundToInt(Vector3.Distance(pos, cercano.transform.position));
                var etiquetas = new List<string>();
                if (kv.Value.Count > 1) etiquetas.Add("x" + kv.Value.Count);
                if (cercano.Tier != MonumentTier.Tier0) etiquetas.Add(cercano.Tier.ToString().Replace("Tier", "Tier "));
                if (cercano.IsSafeZone) etiquetas.Add("zona segura");
                etiquetas.Add(dist >= 1000 ? (dist / 1000f).ToString("0.0") + " km" : dist + " m");

                salida.Add(new Entrada
                {
                    Titulo = kv.Key,
                    Etiqueta = string.Join("  ·  ", etiquetas.ToArray()),
                    Texto = DescripcionDe(kv.Key),
                    Pos = cercano.transform.position,
                });
            }
            return salida.OrderBy(e => Vector3.Distance(pos, e.Pos)).ToList();
        }

        // ─────────────────────────────────────────────────────────────
        //  Secciones fijas
        // ─────────────────────────────────────────────────────────────
        static readonly List<Entrada> Eventos = new List<Entrada>
        {
            new Entrada { Titulo = "Excavadora gigante", Etiqueta = "monumento · diesel",
                Texto = "1) Consigue <color=#ff0>diesel</color> (/menu, busca diesel).\n2) En el edificio de control elige el recurso: piedra, metal, azufre o HQM.\n3) Echa el diesel en el motor y dale al boton rojo.\n4) Recoge el material en las cintas transportadoras.\nCada barril dura un par de minutos. Aqui la produccion va <color=#7f7>x3</color>." },
            new Entrada { Titulo = "Patrol Helicopter", Etiqueta = "evento · pelea",
                Texto = "Helicoptero que patrulla y dispara a quien vea. Tirarlo deja cajas quemadas con armas y munición.\nPara llamarlo a donde estas: <color=#ff0>heli.calltome</color> en F1.\nPara que no salga solo: heli.guns 0 o desactivar el evento." },
            new Entrada { Titulo = "Bradley APC", Etiqueta = "launch site · activo",
                Texto = "Tanque que da vueltas por Launch Site y dispara a todo lo que se mueve. Deja cajas muy buenas al destruirlo.\nPara que vuelva ya mismo sin esperar: <color=#ff0>bradley.quickrespawn</color> en F1." },
            new Entrada { Titulo = "Cargo Ship", Etiqueta = "evento · mar · activo",
                Texto = "Barco enorme que da la vuelta al mapa con cientificos y cajas bloqueadas. Se sube con tirolina, helicoptero o barco.\nPara lanzarlo ahora: <color=#ff0>spawn.cargoshipevent</color> en F1." },
            new Entrada { Titulo = "Todos los eventos", Etiqueta = "activos",
                Texto = "Airdrop, Chinook, Cargo, patrulla de F15, Patrol Heli, Bradley de carretera y vendedor ambulante estan <color=#7f7>activados</color> con los tiempos normales de Rust.\n<color=#ff0>events.print_server_events</color> — verlos y cuanto tardan.\n<color=#ff0>eventschedule.triggerevent &lt;nombre&gt;</color> — lanzar uno ya." },
            new Entrada { Titulo = "Airdrop", Etiqueta = "caja del cielo",
                Texto = "Sacate una <color=#ff0>supply signal</color> desde /menu (busca 'supply'), tirala al suelo y un avion suelta una caja de suministros donde cae el humo." },
            new Entrada { Titulo = "Deep sea", Etiqueta = "mar profundo",
                Texto = "Zona de mar profundo con islas, ciudades flotantes, barcos fantasma y patrullas de RHIB.\nSe entra por los <color=#7af>portales</color> que salen en el mar, a 2750 m del centro en cada direccion.\n<color=#ff0>/deepsea on</color> la abre (tarda unos minutos en generarse) y <color=#ff0>/deepsea off</color> la cierra." },
        };

        static readonly List<Entrada> Puzzles = new List<Entrada>
        {
            new Entrada { Titulo = "Como funciona un puzzle", Etiqueta = "fusible + tarjeta",
                Texto = "1) Busca la caja de fusibles y mete un <color=#ff0>fusible electrico</color>.\n2) Dale a los interruptores para dar corriente.\n3) Pasa la tarjeta por el lector: la puerta se abre unos segundos.\nEn este servidor puedes sacar fusibles y tarjetas desde <color=#ff0>/menu</color>." },
            new Entrada { Titulo = "Tarjeta verde", Etiqueta = "keycard_green",
                Texto = "La mas basica. Abre los puzzles pequenos: alcantarillado, antena parabolica, tunel militar...\nSuele aparecer en cajas normales y barriles." },
            new Entrada { Titulo = "Tarjeta azul", Etiqueta = "keycard_blue",
                Texto = "Nivel medio: depuradora, aeropuerto, central electrica, estacion de tren.\nMuchas veces se consigue haciendo antes el puzzle verde del mismo monumento." },
            new Entrada { Titulo = "Tarjeta roja", Etiqueta = "keycard_red",
                Texto = "La mejor: Launch Site, tuneles grandes y las salas con mejor loot.\nSe suele encontrar detras de un puzzle azul." },
            new Entrada { Titulo = "Cajas bloqueadas", Etiqueta = "hackear",
                Texto = "Las cajas azules bloqueadas (oil rig, cargo, Chinook) se hackean: le das y hay que <color=#fb7>esperar</color> mientras baja el contador. Todo el mundo ve el aviso, asi que prepara la defensa." },
            new Entrada { Titulo = "Recicladores", Etiqueta = "chatarra",
                Texto = "Convierten componentes y objetos en recursos y <color=#ff0>chatarra</color>. Estan en casi todos los monumentos medianos, en Outpost y en Bandit Camp.\nAqui tambien tienes <color=#ff0>/scrap &lt;cantidad&gt;</color> para no farmear." },
        };

        static readonly List<Entrada> MetroLabs = new List<Entrada>
        {
            new Entrada { Titulo = "Metro (Train Tunnels)", Etiqueta = "subterraneo",
                Texto = "Red de tuneles que cruza el mapa por debajo. Se entra por las <color=#7af>Train Tunnel</color> del mapa (escaleras o ascensor).\nDentro hay estaciones, barriles, cajas, cientificos y <color=#fb7>vagonetas</color> que se conducen. Cuidado con los trenes que pasan solos." },
            new Entrada { Titulo = "Vagonetas y trenes", Etiqueta = "work cart",
                Texto = "Las vagonetas funcionan con <color=#ff0>low grade fuel</color>: sube, arranca el motor y usa la palanca. Las agujas de las vias cambian el sentido.\nEn superficie hay vias con trenes largos que se pueden enganchar." },
            new Entrada { Titulo = "Deep Sea / Underwater Labs", Etiqueta = "submarino",
                Texto = "Laboratorios en el fondo del mar. Se entra con <color=#7af>submarino</color> por las piscinas interiores (moon pools).\nDentro: puzzles con tarjetas, cientificos, cajas y torpedos. Ojo al oxigeno del submarino." },
            new Entrada { Titulo = "Como llegar a los labs", Etiqueta = "paso a paso",
                Texto = "1) Compra o spawnea un submarino (los venden en <color=#7f7>Fishing Village</color>).\n2) Busca en el mapa las zonas de labs en aguas profundas.\n3) Baja y entra por la piscina; el submarino se queda flotando dentro.\n4) Sal por donde entraste: nadar desde el fondo no suele salir bien." },
            new Entrada { Titulo = "Buceo y oxigeno", Etiqueta = "equipo",
                Texto = "Para el fondo del mar hace falta <color=#ff0>traje de buceo, aletas y bombona</color>. Sin bombona el oxigeno dura poco.\nSacatelo todo desde /menu buscando 'diving'." },
            new Entrada { Titulo = "Cuevas", Etiqueta = "interior",
                Texto = "Repartidas por el mapa, con barriles y cajas. Algunas tienen radiacion ligera.\nSon buen sitio para esconder una base, aunque aqui el servidor es de pruebas." },
        };

        static readonly List<Entrada> Vehiculos = new List<Entrada>
        {
            new Entrada { Titulo = "Attack Helicopter", Etiqueta = "/mm · /attackheli · /heli",
                Texto = "Te lo pone delante <color=#7f7>lleno de combustible, cohetes HV y bengalas</color>.\nAsiento de piloto para volar y el de atras para la torreta y los cohetes." },
            new Entrada { Titulo = "Minicopter y Scrap Heli", Etiqueta = "spawn",
                Texto = "En F1:\n<color=#ff0>spawn minicopter.entity</color>\n<color=#ff0>spawn scraptransporthelicopter</color>\nFuncionan con low grade fuel: metelo en el deposito del morro." },
            new Entrada { Titulo = "Barcos y submarinos", Etiqueta = "mar",
                Texto = "En F1:\n<color=#ff0>spawn rowboat</color> · <color=#ff0>spawn rhib</color>\n<color=#ff0>spawn submarinesolo.entity</color> · <color=#ff0>spawn submarineduo.entity</color>\nTambien se compran en Fishing Village." },
            new Entrada { Titulo = "Coches modulares", Etiqueta = "carretera",
                Texto = "Aparecen por las carreteras y en el desguace. Se les ponen modulos (cabina, motor, cajon) en los elevadores de los talleres.\nNecesitan motor con piezas y low grade fuel." },
            new Entrada { Titulo = "Caballos", Etiqueta = "campo",
                Texto = "Se compran en el rancho o los establos. Comen maiz y hierba.\nEn F1: <color=#ff0>spawn testridablehorse</color>." },
            new Entrada { Titulo = "Globo aerostatico", Etiqueta = "hot air balloon",
                Texto = "Sube echando combustible al quemador; se mueve con el viento y con los motores si le pones.\nEn F1: <color=#ff0>spawn hotairballoon</color>." },
        };

        static readonly List<Entrada> Comandos = new List<Entrada>
        {
            new Entrada { Titulo = "Menus del servidor", Etiqueta = "chat",
                Texto = "<color=#ff0>/menu</color> o /items — spawner de todos los items, con skins.\n<color=#ff0>/sk</color> — skins del item que llevas en la mano.\n<color=#ff0>/skin</color> — caja de skins del plugin Skins.\n<color=#ff0>/bskin</color> — skins de bloques de construccion.\n<color=#ff0>/wskin &lt;id&gt;</color> — aplicar una skin por ID." },
            new Entrada { Titulo = "Creativo", Etiqueta = "chat",
                Texto = "<color=#ff0>/mm</color> · /attackheli — attack heli cargado.\n<color=#ff0>/scrap &lt;cantidad&gt;</color> — chatarra.\n<color=#ff0>/repair</color> — abre una mesa de reparacion donde estes.\n<color=#ff0>/limpiar</color> — vaciar el inventario.\n<color=#ff0>/crafteo</color> — crafteo gratis e instantaneo on/off.\n<color=#ff0>/unlockall</color> — desbloquear blueprints." },
            new Entrada { Titulo = "Quitar construcciones", Etiqueta = "/remove",
                Texto = "<color=#ff0>/remove</color> enciende el modo quitar: apunta a una pared, un suelo o cualquier objeto puesto y dale al <color=#ff0>clic izquierdo</color> para borrarlo, sea de quien sea.\nOtra vez <color=#ff0>/remove</color> y se apaga.\nSolo borra construcciones y objetos: ni jugadores ni bichos ni vehiculos." },
            new Entrada { Titulo = "Hora del mapa", Etiqueta = "/hora",
                Texto = "<color=#ff0>/hora</color> — ver la hora y si el tiempo corre.\n<color=#ff0>/hora 12</color> — poner esa hora (0 a 24).\n<color=#ff0>/hora dia|noche|amanecer|atardecer</color>.\n<color=#ff0>/hora parar</color> — congelar la hora.\n<color=#ff0>/hora auto</color> — que vuelva a correr." },
            new Entrada { Titulo = "Deep sea", Etiqueta = "/deepsea",
                Texto = "La zona de mar profundo con islas, ciudades flotantes y barcos fantasma.\n<color=#ff0>/deepsea on</color> — activarla y abrirla (tarda un rato en generarse).\n<color=#ff0>/deepsea off</color> — cerrarla y desactivarla.\n<color=#ff0>/deepsea</color> — ver el estado en la consola.\nSe entra por los portales que salen en el mar, en los bordes del mapa." },
            new Entrada { Titulo = "Volar y moverse", Etiqueta = "F1",
                Texto = "<color=#ff0>noclip</color> — volar y atravesar paredes.\n<color=#ff0>teleportpos x y z</color> — ir a unas coordenadas.\n<color=#ff0>teleport2marker</color> — ir a la marca del mapa (G, clic derecho).\n<color=#ff0>debugcamera</color> — camara libre." },
            new Entrada { Titulo = "God y vanish", Etiqueta = "chat",
                Texto = "<color=#ff0>/god</color> — invulnerable.\n<color=#ff0>/vanish</color> — invisible para todo lo demas.\nUtiles para mirar monumentos sin que te maten los cientificos." },
            new Entrada { Titulo = "Items por consola", Etiqueta = "F1",
                Texto = "<color=#ff0>inventory.give &lt;item&gt; &lt;cantidad&gt;</color>\n<color=#ff0>inventory.give rifle.ak 1 1 &lt;skinID&gt;</color> — con skin puesta.\nLa lista completa esta en docs/ITEMS.md y docs/SKINS.md del repo." },
            new Entrada { Titulo = "Panel web", Etiqueta = "navegador",
                Texto = "<color=#7af>http://127.0.0.1:28080</color>\nMapa, configuracion del servidor, permisos, plugins, jugadores, graficas y consola en directo.\nSe abre solo con start.bat o con RGL.bat." },
        };

        List<Entrada> EntradasDe(int seccion, BasePlayer player)
        {
            switch (seccion)
            {
                case 0: return Monumentos(player);
                case 1: return Eventos;
                case 2: return Puzzles;
                case 3: return MetroLabs;
                case 4: return Vehiculos;
                default: return Comandos;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Comandos
        // ─────────────────────────────────────────────────────────────
        [ChatCommand("help")]
        void CmdHelp(BasePlayer player, string cmd, string[] args) => Abrir(player);

        [ChatCommand("ayuda")]
        void CmdAyuda(BasePlayer player, string cmd, string[] args) => Abrir(player);

        [ChatCommand("guia")]
        void CmdGuia(BasePlayer player, string cmd, string[] args) => Abrir(player);

        void Abrir(BasePlayer player)
        {
            var s = St(player);
            s[1] = 0;
            Dibujar(player);
        }

        void Dibujar(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiRoot);
            CuiHelper.AddUi(player, Construir(player, St(player)));
        }

        CuiElementContainer Construir(BasePlayer player, int[] st)
        {
            var seccion = Mathf.Clamp(st[0], 0, Secciones.Length - 1);
            var entradas = EntradasDe(seccion, player);
            var maxPage = Math.Max(0, (entradas.Count - 1) / PerPage);
            var page = Mathf.Clamp(st[1], 0, maxPage);
            st[0] = seccion;
            st[1] = page;

            var c = new CuiElementContainer();

            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.55" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            c.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "guia.close" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UiRoot);

            c.Add(new CuiPanel
            {
                Image = { Color = ColBack },
                RectTransform = { AnchorMin = "0.08 0.08", AnchorMax = "0.92 0.93" }
            }, UiRoot, UiMain);

            // ── cabecera
            c.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.925", AnchorMax = "1 1" }
            }, UiMain, "guia.head");
            c.Add(new CuiPanel
            {
                Image = { Color = ColAccent },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 0.925" }
            }, UiMain);

            c.Add(new CuiLabel
            {
                Text = { Text = "GUIA DE RUST", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = ColText },
                RectTransform = { AnchorMin = "0.015 0", AnchorMax = "0.4 1" }
            }, "guia.head");

            c.Add(new CuiLabel
            {
                Text = { Text = entradas.Count + " fichas  ·  pagina " + (page + 1) + "/" + (maxPage + 1),
                         FontSize = 12, Align = TextAnchor.MiddleRight, Color = ColDim },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.86 1" }
            }, "guia.head");

            c.Add(new CuiButton
            {
                Button = { Color = ColClose, Command = "guia.close" },
                Text = { Text = "CERRAR", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = "0.875 0.18", AnchorMax = "0.99 0.82" }
            }, "guia.head");

            // ── secciones
            c.Add(new CuiPanel
            {
                Image = { Color = ColPanel },
                RectTransform = { AnchorMin = "0.008 0.015", AnchorMax = "0.2 0.91" }
            }, UiMain, "guia.side");

            var alto = 1f / (Secciones.Length + 1);
            for (var i = 0; i < Secciones.Length; i++)
            {
                var y2 = 1f - i * alto;
                c.Add(new CuiButton
                {
                    Button = { Color = i == seccion ? ColBtnOn : ColCell, Command = "guia.sec " + i },
                    Text = { Text = Secciones[i], FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform =
                    {
                        AnchorMin = "0.06 " + (y2 - alto + 0.006f).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                        AnchorMax = "0.94 " + (y2 - 0.006f).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                    }
                }, "guia.side");
            }

            c.Add(new CuiLabel
            {
                Text = { Text = "/help  ·  /guia\nClic fuera para cerrar", FontSize = 10,
                         Align = TextAnchor.LowerCenter, Color = ColSoft },
                RectTransform = { AnchorMin = "0.06 0.01", AnchorMax = "0.94 " + (alto - 0.01f).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) }
            }, "guia.side");

            // ── fichas
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.21 0.09", AnchorMax = "0.992 0.91" }
            }, UiMain, UiGrid);

            if (entradas.Count == 0)
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = seccion == 0 ? "No se han podido leer los monumentos del mapa." : "Sin fichas.",
                             FontSize = 15, Align = TextAnchor.MiddleCenter, Color = ColDim },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, UiGrid);
            }

            var anchoCelda = 1f / Cols;
            var altoCelda = 1f / Rows;
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            for (var i = 0; i < PerPage; i++)
            {
                var idx = page * PerPage + i;
                if (idx >= entradas.Count) break;
                var e = entradas[idx];

                var x1 = (i % Cols) * anchoCelda;
                var y2 = 1f - (i / Cols) * altoCelda;
                var cell = "guia.cell." + i;

                c.Add(new CuiPanel
                {
                    Image = { Color = ColCell },
                    RectTransform =
                    {
                        AnchorMin = (x1 + 0.006f).ToString("0.####", inv) + " " + (y2 - altoCelda + 0.014f).ToString("0.####", inv),
                        AnchorMax = (x1 + anchoCelda - 0.006f).ToString("0.####", inv) + " " + (y2 - 0.014f).ToString("0.####", inv)
                    }
                }, UiGrid, cell);

                c.Add(new CuiLabel
                {
                    Text = { Text = e.Titulo, FontSize = 14, Align = TextAnchor.UpperLeft, Color = ColText },
                    RectTransform = { AnchorMin = "0.04 0.78", AnchorMax = "0.96 0.97" }
                }, cell);

                c.Add(new CuiLabel
                {
                    Text = { Text = e.Etiqueta ?? "", FontSize = 10, Align = TextAnchor.UpperLeft, Color = ColAccent },
                    RectTransform = { AnchorMin = "0.04 0.66", AnchorMax = "0.96 0.79" }
                }, cell);

                c.Add(new CuiLabel
                {
                    Text = { Text = e.Texto, FontSize = 11, Align = TextAnchor.UpperLeft, Color = ColDim },
                    RectTransform = { AnchorMin = "0.04 0.06", AnchorMax = "0.96 0.66" }
                }, cell);

                if (e.Pos != Vector3.zero && player.IsAdmin)
                {
                    c.Add(new CuiButton
                    {
                        Button = { Color = ColGreen, Command = "guia.ir " + e.Pos.x.ToString("0.##", inv) + " " + e.Pos.y.ToString("0.##", inv) + " " + e.Pos.z.ToString("0.##", inv) },
                        Text = { Text = "IR ALLI", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColText },
                        RectTransform = { AnchorMin = "0.72 0.02", AnchorMax = "0.96 0.13" }
                    }, cell);
                }
            }

            // ── paginacion
            if (page > 0)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColBtn, Command = "guia.page " + (page - 1) },
                    Text = { Text = "< ANTERIOR", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.21 0.02", AnchorMax = "0.36 0.075" }
                }, UiMain);
            }
            if (page < maxPage)
            {
                c.Add(new CuiButton
                {
                    Button = { Color = ColBtn, Command = "guia.page " + (page + 1) },
                    Text = { Text = "SIGUIENTE >", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                    RectTransform = { AnchorMin = "0.84 0.02", AnchorMax = "0.992 0.075" }
                }, UiMain);
            }

            return c;
        }

        // ─────────────────────────────────────────────────────────────
        //  Botones
        // ─────────────────────────────────────────────────────────────
        [ConsoleCommand("guia.close")]
        void CcClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            CuiHelper.DestroyUi(p, UiRoot);
        }

        [ConsoleCommand("guia.sec")]
        void CcSec(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            var s = St(p);
            s[0] = arg.GetInt(0, 0);
            s[1] = 0;
            Dibujar(p);
        }

        [ConsoleCommand("guia.page")]
        void CcPage(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null) return;
            St(p)[1] = arg.GetInt(0, 0);
            Dibujar(p);
        }

        [ConsoleCommand("guia.ir")]
        void CcIr(ConsoleSystem.Arg arg)
        {
            var p = arg.Player(); if (p == null || !p.IsAdmin) return;
            var pos = new Vector3(arg.GetFloat(0, 0f), arg.GetFloat(1, 0f), arg.GetFloat(2, 0f));
            if (pos == Vector3.zero) return;

            // Un poco por encima del suelo del monumento, para no aparecer dentro de una roca
            var destino = pos + Vector3.up * 3f;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 200f, Vector3.down, out hit, 400f, LayerMask.GetMask("Terrain", "World", "Construction")))
                destino = hit.point + Vector3.up * 2f;

            p.Teleport(destino);
            p.ChatMessage("<color=#8cf>[Guia]</color> Teletransportado. Usa <color=#ff0>noclip</color> si te quedas atascado.");
            CuiHelper.DestroyUi(p, UiRoot);
        }
    }
}
