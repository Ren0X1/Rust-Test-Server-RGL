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

## 🎨 Comandos de skins

| Comando | Qué hace |
|---|---|
| `/skin` · `/skins` | 📦 **El skin box.** Con un item en la mano, abre la lista de skins del workshop y la aplica. |
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

- 🗺️ **Mapa**: procedural, `worldsize 1000` (el mínimo que permite Rust) con `seed 1337`.
  Los mapas pequeños clásicos (*Craggy Island*, *Barren*) **ya no vienen** en las builds actuales,
  así que 1000 es lo más rápido posible.
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
| **CreativeSetup** | ⭐ *Propio.* Desbloquea blueprints al entrar, fija el mediodía y concede los permisos. |

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
