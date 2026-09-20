# 🎨 Rust Test Server RGL

> Servidor dedicado de **Rust** en local, en **modo creativo**, con un mapa de tamaño normal (4000).
> Pensado para una sola cosa: **probar skins** sin farmear nada. 🧪

---

## ⚡ Inicio rápido

**Todo en uno: doble clic en `RGL.bat`.** Un menú con todo:

| Opción | Qué hace |
|---|---|
| **1** | ▶️ Arrancar servidor + panel web *(se elige sola a los 10 s)* |
| **2** | 🔄 Actualizar servidor y Oxide, y arrancar |
| **3** | 🧩 Instalar o reparar todo *(SteamCMD + servidor + Oxide, ~6 GB)* |
| **4** | 🖥️ Abrir solo el panel web |
| **5** | 🧩 Actualizar solo Oxide |

Arriba del menú te dice qué tienes instalado y qué te falta. Luego, en Rust pulsa **F1** y
escribe `client.connect localhost:28015`.

*(Los `.bat` sueltos de siempre siguen ahí: `instalar.bat`, `start.bat`, `panel.bat`,
`actualizar_servidor.bat` y `actualizar_oxide.bat`.)*

> 🖥️ `start.bat` abre también el **panel web** en `http://127.0.0.1:28080` (ver abajo).
> ⏱️ El primer arranque tarda **varios minutos** porque genera el mapa (4000 es un mapa grande).
> Los siguientes son mucho más rápidos porque cargan el save.
> ❌ Para cerrarlo: cierra la ventana de `start.bat`.

---

## 🖥️ Panel web · RGL Control

Panel de control del servidor en el navegador, estilo retro lo-fi. **Se abre solo con
`start.bat`** (o a mano con `panel.bat`) en **http://127.0.0.1:28080**.

| Sección | Qué hace |
|---|---|
| **Panel** | Estado, jugadores, FPS, entidades y memoria, con **gráficas en directo** (FPS, jugadores, entidades, memoria y red; 5 min / 15 min / 1 h, con tabla). Botones de encender, guardar, reiniciar y apagar. |
| **Mapa** | Seed y tamaño con **vista previa de RustMaps** (tierra, islas, ríos, monumentos) y render del mapa real del servidor. Lista de mapas guardados. |
| **Servidor** | Argumentos de `start.bat` y `server.cfg` por secciones, con interruptores. **Guardar y aplicar en vivo** manda las convars cambiadas por RCON sin reiniciar. |
| **Permisos** | Grupos de Oxide con todos los permisos como interruptores, permisos por SteamID y los admins/moderadores de `users.cfg`. |
| **Plugins** | Cargar, recargar y descargar plugins, y editor de sus configs (formulario o JSON) con **guardar y recargar**. |
| **Jugadores** | Conectados, con dar item, expulsar y banear. |
| **Consola** | Consola RCON **en directo** con filtros, búsqueda, historial (↑ ↓) y comandos rápidos. |

- Solo necesita **Node.js** (sin `npm install`). Si no está, `start.bat` arranca el servidor igual.
- Solo escucha en `127.0.0.1`: no se puede abrir desde otro PC. Cada vez que arranca genera
  un token nuevo, así que ninguna otra web abierta en el navegador puede mandarle comandos.
- **Apagar** desde el panel apaga de verdad (`start.bat` no lo reinicia); **Reiniciar** sí.

---

## 🖱️ Menus con interfaz

Dos menus propios, sin salir del juego:

| Comando | Qué abre |
|---|---|
| `/menu` · `/items` | 📦 **Spawner de items.** Los **1252 items** con su icono, ordenados por categoría, con buscador y selector de cantidad (x1 / x10 / x100 / x1000 / stack completo). Clic en un item y te lo da. |
| `/sk` · `/skinmenu` | 🎨 **Skins del item que llevas en la mano.** Cada skin se previsualiza con su icono real, con buscador por nombre o ID y botón para quitarla. Clic y se aplica al instante. |

En el spawner:

- 🎨 **ELEGIR SKIN** en cada item que tenga skins: abre el selector y te da el item **ya con
  la skin puesta**, sin tener que sacarlo y usar `/sk`.
- 🕘 Pestaña **RECIENTES** con lo último que has sacado, y **CON SKINS** para ver solo los
  items que tienen skins. Las categorías van en español y con su número de items.
- 🔢 Cantidad x1 / x10 / x100 / x1000 / STACK o **la que escribas**. Los items que no se
  apilan salen de uno en uno (x10 rifles = 10 rifles), hasta 30 stacks por clic.
- 🔎 El buscador ordena por relevancia y acepta nombre, shortname o itemid.
- 🗑️ **LIMPIAR INVENTARIO** te lo borra todo (inventario, cinturón y ropa). Pide un
  segundo clic de confirmación para que no se pulse sin querer.
- Clic fuera del panel para cerrarlo.

---

## 🧰 Comandos de creativo

| Comando | Qué hace |
|---|---|
| `/help` · `/guia` | 📖 **Guía del juego**: los monumentos **de tu mapa** (con distancia y botón para teletransportarte), eventos, puzzles y tarjetas, metro, labs submarinos, vehículos y comandos. |
| `/mm` · `/attackheli` · `/heli` | 🚁 Te pone un **attack heli** delante, con el depósito lleno y el lanzacohetes cargado de **cohetes HV y bengalas** hasta arriba. |
| `/remove` | 🔨 **Modo quitar**: apunta y clic izquierdo para borrar paredes, suelos y objetos, sean de quien sean. Otra vez `/remove` para salir. |
| `/repair` | 🔧 Abre una **mesa de reparación** donde estés *(también cambia skins)*. |
| `/hora` | 🕛 Hora del mapa: `/hora 12`, `/hora dia|noche|amanecer|atardecer`, `/hora parar`, `/hora auto`. |
| `/deepsea` | 🌊 **Deep sea** (islas, ciudades flotantes y barcos fantasma): `/deepsea on` la abre, `/deepsea off` la cierra, `/deepsea` dice cómo está. |
| `/scrap <cantidad>` | 🔩 Te da esa cantidad de chatarra. Ej: `/scrap 5000` *(máx. 100 000 de golpe)*. |
| `/limpiar` | 🗑️ Borra todo tu inventario (lo mismo que el botón del `/menu`). |
| `/crafteo` | ⚡ Activa o desactiva el **crafteo gratis e instantáneo** *(de fábrica viene activado)*. |

---

## 🎨 Comandos de skins

| Comando | Qué hace |
|---|---|
| `/skin` · `/skins` | 📦 Skin box del plugin Skins *(alimentado por SkinTestMenu: de fábrica viene vacío)* |
| `/bskin` | 🧱 Menú de skins para bloques de construcción. |
| `/wskin <id>` | 🔎 Aplica una skin concreta del workshop por su ID (la del enlace de Steam). |

Y desde la consola **F1**, comandos nativos de Rust:

```bash
inventory.give rifle.ak 1 1 <skinID>   # 🎁 Te da un item ya con esa skin puesta
global.skin_looking <skinID>           # 👁️ Cambia la skin de lo que estas mirando
global.skin_radius <skinID> <radio>    # 💫 Cambia la skin de todo lo de alrededor
workshop.print_approved_skins          # 📋 Lista todas las skins aprobadas
global.print_wallpaper_skins           # 🖼️ Lista las skins de papel pintado
```

---

## 🛠️ Spawnear y craftear lo que quieras

Eres **owner** (auth level 2), así que lo tienes todo abierto:

- 🖱️ **F1 → pestaña de items**: buscador visual para spawnear cualquier cosa.
- ⌨️ `inventory.give <item> <cantidad>` → por ejemplo `inventory.give wood 10000`
- 👥 `inventory.giveto <item> <jugador> <cantidad> <skin>`
- 🔓 **Todos los blueprints se desbloquean solos al entrar.** Si hiciera falta: `/unlockall`
- ⚡ **Crafteo gratis e instantáneo** (plugin CreativeTools, se quita con `/crafteo`): no gasta
  materiales y el item sale al momento, sin cola. *(El `craft.instant` nativo no vale: solo
  funciona para admins y aun así tarda 1 s por unidad.)* El modo creativo, además, quita el
  coste de recursos al construir.

### 🧰 Otros comandos útiles

```bash
noclip              # 🕊️ Volar y atravesar paredes
debugcamera         # 🎥 Camara libre (ideal para mirar skins de cerca)
teleportpos x y z   # 📍 Teletransporte
/god                # 🛡️ Invulnerabilidad
/vanish             # 👻 Invisible para todo lo demas
/dia                # ☀️ Volver a fijar el mediodia si algo lo cambia
```

---

## ⚙️ Cómo está configurado

- 🗺️ **Mapa**: procedural de tamaño normal, `worldsize 4000` con `seed 1219563660`, con
  todos los monumentos grandes (Launch Site, Outpost, Bandit Camp, Oil Rigs, Cargo, Harbor,
  Excavadora...). Se cambia desde la sección **Mapa** del panel web, con vista previa.
  ⚠️ Si buscas otro mapa en RustMaps, fíjate en que la URL sea del tipo `/map/<tamaño>_<seed>`.
  Los que tienen una URL con un hash (`/map/97d3ed70...`) son **mapas custom** hechos con el
  generador de RustMaps: aunque pongas su seed y tamaño, el servidor genera otro mapa distinto.
  Los mapas pequeños clásicos (*Craggy Island*, *Barren*) **ya no vienen** en las builds actuales.
  ⚠️ Si lo vuelves a achicar, **no bajes de 1500**: a 1000 (el mínimo que acepta Rust) el mapa sale casi todo océano
  — solo un 8,6% de tierra — y no genera ni un punto de spawn válido, así que apareces
  bajo el agua en (0,-15,0) y el antihack te expulsa. A 1500 hay un 18,3% de tierra
  y los spawns funcionan.
- 🛡️ **Antihack desactivado** (`antihack.enforcementlevel 0`, `terrain_protection 0`):
  sin esto el `noclip` y volar te sacan del servidor por *InsideTerrain*.
- 🏗️ **Modo creativo nativo para todo el servidor** (`creative.allusers`): construir gratis,
  colocar sin restricciones y sin coste de recursos.
- ☀️ **Siempre mediodía** para ver bien las skins (`env.time 12`, sin paso del tiempo).
- 🕊️ Sin decay, sin radiación, sin colapso de estructuras, sin NPCs ni eventos.
  ⚔️ **PvP activado** (`server.pve false`): los jugadores se pueden hacer daño entre ellos.
- ✖️3️⃣ **Servidor x3** (plugin ServerRates): recolección (árboles, piedras, animales, plantas,
  lo que se recoge del suelo, canteras, excavadora) y loot de barriles y cajas multiplicados
  por 3. Al **romper un barril** el loot va directo a tu inventario, no cae al suelo.
  **Stacks**: los recursos base (madera, piedra, metal, HQM, azufre, pólvora, chatarra, tela,
  cuero, carbón, combustible...) apilan **100 000**; todo lo demás que se apila, **x5**
  (armas y ropa siguen a 1). Todo se cambia en `server\oxide\config\ServerRates.json`.

### 📁 Ficheros que puedes tocar

| Ruta | Para qué |
|---|---|
| `start.bat` | 🔌 Puerto, seed, tamaño del mapa, nombre del servidor *(o desde el panel web)*. |
| `panel\` | 🖥️ Panel web: `server.js` (backend) y `public\` (interfaz). |
| `RGL.bat` | 🎛️ El menú todo en uno: instalar, actualizar, arrancar y panel. |
| `server\server\skintest\cfg\server.cfg` | 🌍 Ajustes del mundo (se ejecuta al arrancar). |
| `server\server\skintest\cfg\users.cfg` | 👑 Quién es admin *(lleva un SteamID64, cámbialo por el tuyo si clonas el repo)*. |
| `server\oxide\plugins\` | 🧩 Plugins. Suelta un `.cs` aquí y se carga solo, **sin reiniciar**. |
| `server\oxide\config\` | 🔧 Configuración de cada plugin (se genera sola). |

> ℹ️ Si cambias el **seed** o el **worldsize** en `start.bat`, se genera un mapa nuevo solo:
> el save se llama `proceduralmap.<tamaño>.<seed>.*.sav`, así que el viejo simplemente deja de usarse.
> Para ver un seed antes de usarlo: `https://rustmaps.com/map/<tamaño>_<seed>` (si alguien lo ha generado ya).
> Puedes borrar los `proceduralmap.*` viejos de `server\server\skintest\` para liberar espacio,
> pero **no borres la carpeta entera**: dentro está `cfg\` con `server.cfg` y `users.cfg`.

### 🧩 Plugins instalados

| Plugin | Para qué |
|---|---|
| **Skins** *(misticos)* | 📦 El skin box (`/skin`) |
| **BuildingSkins** *(Marat)* | 🧱 Skins de bloques de construcción (`/bskin`) |
| **WorkshopSkinViewer** *(Orange)* | 🔎 Aplicar una skin por ID (`/wskin`) |
| **ImageLibrary** | 🖼️ Dependencia de BuildingSkins |
| **Godmode** | 🛡️ `/god` |
| **Vanish** | 👻 `/vanish` |
| **SkinTestMenu** | ⭐ *Propio.* Los menús `/menu` y `/sk`, el exportador de Markdown, y alimenta con skins al plugin Skins vía `OnSkinsFetch` |
| **CreativeSetup** | ⭐ *Propio.* Desbloquea blueprints al entrar, fija el mediodía y concede los permisos. |
| **CreativeTools** | ⭐ *Propio.* `/mm`, `/scrap`, `/remove`, `/repair`, `/hora`, `/deepsea` y el crafteo gratis e instantáneo (`/crafteo`). |
| **GuiaRust** | ⭐ *Propio.* La guía `/help` con los monumentos del mapa y cómo funciona cada cosa. |
| **ServerRates** | ⭐ *Propio.* Rates x3, stacks (100K recursos / x5 el resto) y el loot de los barriles directo al inventario. |

---

## 📖 Listas completas (Markdown)

Por si prefieres copiar y pegar IDs en vez de usar el menú:

| Fichero | Contenido |
|---|---|
| [`docs/SKINS.md`](docs/SKINS.md) | 🎨 Las **6266 skins** del juego con su ID de workshop, agrupadas por item y con índice. |
| [`docs/ITEMS.md`](docs/ITEMS.md) | 📦 Los **1252 items** con `shortname`, `itemid` y cuántas skins tiene cada uno. |

Se generan solos desde los datos del propio servidor. Para regenerarlos tras un
parche de Rust, ejecuta en la consola **F1**:

```bash
skintest.exportmd
```

---

## 🔄 Mantenimiento

Cuando Facepunch saque un parche y el servidor deje de arrancar:
**`RGL.bat` → opción 2** (actualiza el servidor, luego Oxide, y arranca).

A mano es lo mismo en dos pasos:

1. ▶️ `actualizar_servidor.bat`
2. ▶️ `actualizar_oxide.bat` ← **siempre después**, porque el paso 1 sobrescribe Oxide.

---

## 📚 Extra

- 📖 **`comandos_rust_referencia.txt`** — los **2210 comandos y convars** de esta build con su
  descripción. Un `Ctrl+F` ahí suele resolver cualquier duda.
- 🖥️ **RCON** — puerto `28016`, contraseña `skintest`. Solo si quieres usar algo tipo RustAdmin.
  Cámbiala en `start.bat` si algún día abres el servidor fuera de tu PC.

---

## 📦 Qué hay en el repo

Los binarios del servidor (~5,8 GB) **no están versionados** — los descarga `instalar.bat`.
En el repo solo va lo que importa: los scripts, la configuración y los plugins. ✅

---

## 🏷️ Releases

Hay un workflow de GitHub Actions que empaqueta el proyecto y publica una release.

```bash
git tag v1.0.0
git push origin v1.0.0
```

También se puede lanzar a mano desde la pestaña **Actions → Release → Run workflow**
indicando la versión. El zip que sube lleva los scripts, la config, los plugins y los
`docs/` — pero no los ~6 GB de binarios, que los baja `instalar.bat`. 📦
