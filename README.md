# 🎨 Rust Test Server RGL

> Servidor dedicado de **Rust** en local, en **modo creativo**, con mapa mínimo para que cargue rápido.
> Pensado para una sola cosa: **probar skins** sin farmear nada. 🧪

---

## ⚡ Inicio rápido

| | |
|---|---|
| 🧩 **1. Instalar** | Doble clic en **`instalar.bat`** *(descarga SteamCMD + servidor + Oxide, ~6 GB)* |
| ▶️ **2. Arrancar** | Doble clic en **`start.bat`** y espera a `Server startup complete` |
| 🎮 **3. Conectar** | En Rust pulsa **F1** y escribe: `client.connect localhost:28015` |

> ⏱️ El primer arranque tarda **~60-90 s** porque genera el mapa. Los siguientes son más rápidos.
> ❌ Para cerrarlo: cierra la ventana de `start.bat`.

---

## 🖱️ Menus con interfaz

Dos menus propios, sin salir del juego:

| Comando | Qué abre |
|---|---|
| `/menu` · `/items` | 📦 **Spawner de items.** Los **1252 items** con su icono, ordenados por categoría, con buscador y selector de cantidad (x1 / x10 / x100 / x1000 / stack completo). Clic en un item y te lo da. |
| `/sk` · `/skinmenu` | 🎨 **Skins del item que llevas en la mano.** Cada skin se previsualiza con su icono real, con buscador por nombre o ID y botón para quitarla. Clic y se aplica al instante. |

En el spawner, la pestaña **CON SKINS** filtra solo los items que tienen skins,
y cada celda te dice cuántas tiene. 🟢

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
- ⚡ **Craft instantáneo** (`craft.instant`) y el modo creativo quita el coste de recursos al construir.

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

- 🗺️ **Mapa**: procedural, `worldsize 1500` con `seed 1337`.
  Los mapas pequeños clásicos (*Craggy Island*, *Barren*) **ya no vienen** en las builds actuales.
  ⚠️ **No bajes de 1500**: a 1000 (el mínimo que acepta Rust) el mapa sale casi todo océano
  — solo un 8,6% de tierra — y no genera ni un punto de spawn válido, así que apareces
  bajo el agua en (0,-15,0) y el antihack te expulsa. A 1500 hay un 18,3% de tierra
  y los spawns funcionan.
- 🛡️ **Antihack desactivado** (`antihack.enforcementlevel 0`, `terrain_protection 0`):
  sin esto el `noclip` y volar te sacan del servidor por *InsideTerrain*.
- 🏗️ **Modo creativo nativo para todo el servidor** (`creative.allusers`): construir gratis,
  colocar sin restricciones y sin coste de recursos.
- ☀️ **Siempre mediodía** para ver bien las skins (`env.time 12`, sin paso del tiempo).
- 🕊️ Sin decay, sin radiación, sin colapso de estructuras, PvE, sin NPCs ni eventos.

### 📁 Ficheros que puedes tocar

| Ruta | Para qué |
|---|---|
| `start.bat` | 🔌 Puerto, seed, tamaño del mapa, nombre del servidor. |
| `server\server\skintest\cfg\server.cfg` | 🌍 Ajustes del mundo (se ejecuta al arrancar). |
| `server\server\skintest\cfg\users.cfg` | 👑 Quién es admin *(lleva un SteamID64, cámbialo por el tuyo si clonas el repo)*. |
| `server\oxide\plugins\` | 🧩 Plugins. Suelta un `.cs` aquí y se carga solo, **sin reiniciar**. |
| `server\oxide\config\` | 🔧 Configuración de cada plugin (se genera sola). |

> ⚠️ Si cambias el **seed** o el **worldsize** en `start.bat`, borra la carpeta
> `server\server\skintest\` para que no intente cargar el save del mapa viejo.

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

---

## 📖 Listas completas (Markdown)

Por si prefieres copiar y pegar IDs en vez de usar el menú:

| Fichero | Contenido |
|---|---|
| [`docs/SKINS.md`](docs/SKINS.md) | 🎨 Las **5800 skins** del juego con su ID, agrupadas por item y con índice. |
| [`docs/ITEMS.md`](docs/ITEMS.md) | 📦 Los **1252 items** con `shortname`, `itemid` y cuántas skins tiene cada uno. |

Se generan solos desde los datos del propio servidor. Para regenerarlos tras un
parche de Rust, ejecuta en la consola **F1**:

```bash
skintest.exportmd
```

---

## 🔄 Mantenimiento

Cuando Facepunch saque un parche y el servidor deje de arrancar:

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
