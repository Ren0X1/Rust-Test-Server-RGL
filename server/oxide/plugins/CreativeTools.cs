using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CreativeTools", "local", "1.2.0")]
    [Description("Attack heli cargado, /scrap, crafteo gratis, hora del mapa, deep sea, /remove y /repair")]
    class CreativeTools : RustPlugin
    {
        const string PrefabAttackHeli = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab";
        const int MaxScrap = 100000;

        // ─────────────────────────────────────────────────────────────
        //  Configuracion (oxide/config/CreativeTools.json)
        // ─────────────────────────────────────────────────────────────
        class Configuracion
        {
            public bool CrafteoGratisEInstantaneo = true;

            // Municion del attack heli. 0 = usar el tamano de stack del item.
            // Las bengalas apilan de 25 en 25, asi que para llenar el hueco de
            // verdad hay que pasarse del stack (el contenedor lo admite).
            public int CohetesPorHueco = 0;
            public int Bengalas = 250;

            // Alcance del /remove, en metros
            public float AlcanceRemove = 12f;
        }

        Configuracion cfg;

        protected override void LoadDefaultConfig() => cfg = new Configuracion();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { cfg = Config.ReadObject<Configuracion>(); } catch { cfg = null; }
            if (cfg == null) cfg = new Configuracion();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(cfg, true);

        void Msg(BasePlayer p, string texto) => p.ChatMessage("<color=#8cf>[Creativo]</color> " + texto);

        // Da la cantidad que sea partiendola en stacks validos
        void DarItem(BasePlayer p, ItemDefinition def, int total, ulong skin = 0)
        {
            var stack = Math.Max(1, def.stackable);
            for (var resto = total; resto > 0; resto -= stack)
            {
                var item = ItemManager.Create(def, Math.Min(resto, stack), skin);
                if (item != null) p.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  /attackheli  ·  /heli
        // ─────────────────────────────────────────────────────────────
        [ChatCommand("attackheli")]
        void CmdAttackHeli(BasePlayer player, string cmd, string[] args)
        {
            // Delante del jugador, mirando hacia donde mira el, apoyado en el suelo
            var rotacion = Quaternion.Euler(0f, player.eyes.rotation.eulerAngles.y, 0f);
            var pos = player.transform.position + rotacion * Vector3.forward * 8f;

            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out hit, 100f,
                    LayerMask.GetMask("Terrain", "World", "Construction", "Deployed")))
                pos = hit.point;
            pos.y += 0.5f;

            var heli = GameManager.server.CreateEntity(PrefabAttackHeli, pos, rotacion) as PlayerHelicopter;
            if (heli == null) { Msg(player, "No se pudo crear el attack heli."); return; }

            heli.OwnerID = player.userID;
            heli.Spawn();

            // El deposito y el lanzacohetes son sub-entidades que se crean al
            // spawnear: se llenan un instante despues
            timer.Once(0.5f, () =>
            {
                if (heli == null || heli.IsDestroyed) return;
                heli.GetFuelSystem()?.FillFuel();
                var cargado = CargarMuniciones(heli as AttackHelicopter);
                Msg(player, cargado
                    ? "Attack heli listo: <color=#7f7>combustible, cohetes HV y bengalas al maximo</color>."
                    : "Attack heli creado con el deposito lleno (no se pudo cargar la municion).");
            });
        }

        [ChatCommand("heli")]
        void CmdHeli(BasePlayer player, string cmd, string[] args) => CmdAttackHeli(player, cmd, args);

        [ChatCommand("mm")]
        void CmdMm(BasePlayer player, string cmd, string[] args) => CmdAttackHeli(player, cmd, args);

        // El contenedor de cohetes del attack heli reserva el ultimo hueco para
        // las bengalas y el resto para cohetes. Se llena cada hueco a tope.
        bool CargarMuniciones(AttackHelicopter heli)
        {
            if (heli == null) return false;
            var cohetes = heli.GetRockets();
            if (cohetes == null || cohetes.inventory == null) return false;

            var inv = cohetes.inventory;
            var defCohete = cohetes.hvRocketDef;
            var defBengala = cohetes.flareItemDef;
            if (defCohete == null || defBengala == null) return false;

            var huecos = inv.capacity;
            for (var slot = 0; slot < huecos; slot++)
            {
                if (inv.GetSlot(slot) != null) continue;
                var bengalas = slot == huecos - 1;                       // ultimo hueco: bengalas
                var def = bengalas ? defBengala : defCohete;
                var cantidad = bengalas
                    ? Math.Max(1, cfg.Bengalas)
                    : (cfg.CohetesPorHueco > 0 ? cfg.CohetesPorHueco : Math.Max(1, def.stackable));

                var item = ItemManager.Create(def, cantidad);
                if (item == null) continue;
                if (!item.MoveToContainer(inv, slot, false)) item.Remove();
            }

            inv.MarkDirty();
            cohetes.SendNetworkUpdate();
            return true;
        }

        // ─────────────────────────────────────────────────────────────
        //  /scrap <cantidad>
        // ─────────────────────────────────────────────────────────────
        [ChatCommand("scrap")]
        void CmdScrap(BasePlayer player, string cmd, string[] args)
        {
            int cantidad;
            if (args.Length < 1 || !int.TryParse(args[0], out cantidad) || cantidad < 1)
            {
                Msg(player, "Uso: <color=#ff0>/scrap <cantidad></color>   ej: /scrap 5000");
                return;
            }
            if (cantidad > MaxScrap)
            {
                Msg(player, "Maximo " + MaxScrap + " de golpe (lo que no quepa cae al suelo).");
                cantidad = MaxScrap;
            }

            DarItem(player, ItemManager.FindItemDefinition("scrap"), cantidad);
            Msg(player, "+" + cantidad + " de chatarra.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Crafteo gratis e instantaneo  (/crafteo para activarlo o quitarlo)
        // ─────────────────────────────────────────────────────────────
        //  - CanCraft: saltarse la comprobacion de materiales.
        //  - OnItemCraft: devolver lo que Rust haya cogido y dar el item ya,
        //    sin pasar por la cola (craft.instant solo va para admins y
        //    aun asi tarda 1 s por unidad).
        [ChatCommand("crafteo")]
        void CmdCrafteo(BasePlayer player, string cmd, string[] args)
        {
            cfg.CrafteoGratisEInstantaneo = !cfg.CrafteoGratisEInstantaneo;
            SaveConfig();
            Msg(player, "Crafteo gratis e instantaneo: " +
                (cfg.CrafteoGratisEInstantaneo ? "<color=#7f7>ACTIVADO</color>" : "<color=#f77>DESACTIVADO</color>"));
        }

        // ─────────────────────────────────────────────────────────────
        //  /hora — hora del mapa
        // ─────────────────────────────────────────────────────────────
        void Convar(string clave, string valor) => ConsoleSystem.Run(ConsoleSystem.Option.Server, clave, valor);

        [ChatCommand("hora")]
        void CmdHora(BasePlayer player, string cmd, string[] args)
        {
            if (args.Length == 0)
            {
                Msg(player, "Son las <color=#ff0>" + TOD_Sky.Instance.Cycle.Hour.ToString("0.0") + "</color> h. "
                    + "El tiempo " + (ConVar.Env.progresstime ? "<color=#7f7>avanza</color>" : "esta <color=#f77>parado</color>") + ".");
                Msg(player, "Uso: <color=#ff0>/hora 12</color> · <color=#ff0>/hora dia|noche|amanecer|atardecer</color> · <color=#ff0>/hora auto|parar</color>");
                return;
            }

            var a = args[0].ToLower();
            switch (a)
            {
                case "auto": case "correr": case "on":
                    Convar("env.progresstime", "true");
                    Msg(player, "El tiempo <color=#7f7>vuelve a correr</color>.");
                    return;
                case "parar": case "stop": case "off":
                    Convar("env.progresstime", "false");
                    Msg(player, "Tiempo <color=#f77>congelado</color> a las " + TOD_Sky.Instance.Cycle.Hour.ToString("0.0") + " h.");
                    return;
            }

            float hora;
            switch (a)
            {
                case "dia": case "mediodia": hora = 12f; break;
                case "noche": case "medianoche": hora = 0f; break;
                case "amanecer": hora = 7f; break;
                case "atardecer": case "tarde": hora = 18f; break;
                default:
                    if (!float.TryParse(a.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out hora) || hora < 0f || hora > 24f)
                    {
                        Msg(player, "Pon una hora entre 0 y 24, o dia / noche / amanecer / atardecer.");
                        return;
                    }
                    break;
            }

            Convar("env.time", hora.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Msg(player, "Hora puesta a las <color=#ff0>" + hora.ToString("0.##") + "</color> h"
                + (ConVar.Env.progresstime ? " (el tiempo sigue corriendo: <color=#ff0>/hora parar</color> para congelarlo)." : "."));
        }

        // ─────────────────────────────────────────────────────────────
        //  /deepsea — abrir o cerrar la zona de mar profundo
        // ─────────────────────────────────────────────────────────────
        [ChatCommand("deepsea")]
        void CmdDeepSea(BasePlayer player, string cmd, string[] args)
        {
            var a = args.Length > 0 ? args[0].ToLower() : "estado";

            if (a == "on" || a == "abrir" || a == "activar")
            {
                Convar("deepsea.enabled", "true");
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "deepsea.open");
                Msg(player, "Deep sea <color=#7f7>activado</color> y abriendose. Tarda un rato en generarse.");
                Msg(player, "Si al reiniciar no aparece, es que <color=#ff0>deepsea.enabled</color> hay que ponerlo antes de arrancar (esta en el panel web).");
                return;
            }

            if (a == "off" || a == "cerrar" || a == "desactivar")
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "deepsea.close");
                Convar("deepsea.enabled", "false");
                Msg(player, "Deep sea <color=#f77>cerrandose</color> y desactivado. Al reiniciar ya no saldra.");
                return;
            }

            ConsoleSystem.Run(ConsoleSystem.Option.Server, "deepsea.status");
            Msg(player, "Estado del deep sea impreso en la consola del servidor y en el panel web.");
            Msg(player, "Uso: <color=#ff0>/deepsea on</color> · <color=#ff0>/deepsea off</color>");
        }

        // ─────────────────────────────────────────────────────────────
        //  /remove — quitar construcciones apuntando y haciendo clic
        // ─────────────────────────────────────────────────────────────
        readonly HashSet<ulong> _quitando = new HashSet<ulong>();

        [ChatCommand("remove")]
        void CmdRemove(BasePlayer player, string cmd, string[] args)
        {
            if (_quitando.Remove(player.userID))
            {
                Msg(player, "Modo quitar <color=#f77>desactivado</color>.");
                return;
            }
            _quitando.Add(player.userID);
            Msg(player, "Modo quitar <color=#7f7>activado</color>: apunta y dale al <color=#ff0>clic izquierdo</color> para borrar paredes, suelos y objetos. Otra vez <color=#ff0>/remove</color> para salir.");
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || _quitando.Count == 0) return;
            if (!_quitando.Contains(player.userID)) return;
            if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY)) return;

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, cfg.AlcanceRemove,
                    LayerMask.GetMask("Construction", "Deployed", "Default", "World", "Tree")))
                return;

            var ent = hit.GetEntity();
            if (ent == null || ent.IsDestroyed) return;

            // Solo construcciones y objetos colocados: nada de jugadores, bichos ni vehiculos
            var decay = ent as DecayEntity;
            if (decay == null)
            {
                Msg(player, "Eso no es una construccion (" + ent.ShortPrefabName + ").");
                return;
            }

            var nombre = ent.ShortPrefabName;
            ent.Kill(BaseNetworkable.DestroyMode.Gib);
            Msg(player, "Quitado: <color=#ff0>" + nombre + "</color>");
        }

        // ─────────────────────────────────────────────────────────────
        //  /repair — abre una mesa de reparacion donde estes
        // ─────────────────────────────────────────────────────────────
        const string PrefabRepairBench = "assets/prefabs/deployable/repair bench/repairbench_deployed.prefab";

        [ChatCommand("repair")]
        void CmdRepair(BasePlayer player, string cmd, string[] args)
        {
            // La mesa se crea debajo del suelo: no se ve ni estorba, y se borra al cerrarla
            var pos = player.transform.position + Vector3.down * 6f;
            var mesa = GameManager.server.CreateEntity(PrefabRepairBench, pos, Quaternion.identity) as RepairBench;
            if (mesa == null) { Msg(player, "No se pudo crear la mesa de reparacion."); return; }

            mesa.enableSaving = false;
            mesa.Spawn();

            timer.Once(0.2f, () =>
            {
                if (mesa == null || mesa.IsDestroyed || player == null || !player.IsConnected) { if (mesa != null && !mesa.IsDestroyed) mesa.Kill(); return; }
                player.inventory.loot.PositionChecks = false;
                if (!mesa.PlayerOpenLoot(player, mesa.panelName, false))
                {
                    player.inventory.loot.PositionChecks = true;
                    mesa.Kill();
                    Msg(player, "No se pudo abrir la mesa de reparacion.");
                    return;
                }
                _mesas[player.userID] = mesa;
                Msg(player, "Mesa de reparacion abierta. Tambien sirve para <color=#ff0>cambiar skins</color> de lo que metas.");
            });
        }

        readonly Dictionary<ulong, RepairBench> _mesas = new Dictionary<ulong, RepairBench>();

        void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null) return;
            RepairBench mesa;
            if (!_mesas.TryGetValue(player.userID, out mesa) || mesa != entity) return;
            _mesas.Remove(player.userID);
            player.inventory.loot.PositionChecks = true;
            // Lo que se haya quedado dentro vuelve al jugador
            if (mesa != null && !mesa.IsDestroyed)
            {
                if (mesa.inventory != null)
                    foreach (var item in mesa.inventory.itemList.ToArray())
                    {
                        item.RemoveFromContainer();
                        player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                    }
                mesa.Kill();
            }
        }

        void Unload()
        {
            foreach (var kv in _mesas)
                if (kv.Value != null && !kv.Value.IsDestroyed) kv.Value.Kill();
            _mesas.Clear();
        }

        object CanCraft(ItemCrafter crafter, ItemBlueprint bp, int amount, bool free)
        {
            return cfg.CrafteoGratisEInstantaneo ? (object)true : null;
        }

        object OnItemCraft(ItemCraftTask task, BasePlayer owner, Item fromTempBlueprint)
        {
            if (!cfg.CrafteoGratisEInstantaneo || owner == null) return null;

            if (task.takenItems != null)
            {
                foreach (var cogido in task.takenItems)
                {
                    // El blueprint temporal se gasta, como en vanilla
                    if (cogido == fromTempBlueprint) { cogido.Remove(); continue; }
                    owner.GiveItem(cogido, BaseEntity.GiveItemReason.Generic);
                }
                task.takenItems.Clear();
            }

            var def = task.blueprint.targetItem;
            var skin = ItemDefinition.FindSkin(def.itemid, task.skinID);
            DarItem(owner, def, task.blueprint.amountToCreate * task.amount, skin);

            // true = "crafteado": Rust no mete la tarea en la cola
            return true;
        }
    }
}
