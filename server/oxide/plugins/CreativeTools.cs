using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CreativeTools", "local", "1.0.0")]
    [Description("Attack heli con el deposito lleno, /scrap y crafteo gratis e instantaneo")]
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

            // El deposito es una sub-entidad que se crea al spawnear: se llena un instante despues
            timer.Once(0.5f, () =>
            {
                if (heli == null || heli.IsDestroyed) return;
                heli.GetFuelSystem()?.FillFuel();
            });

            Msg(player, "Attack heli creado con el deposito lleno.");
        }

        [ChatCommand("heli")]
        void CmdHeli(BasePlayer player, string cmd, string[] args) => CmdAttackHeli(player, cmd, args);

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
