using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("ServerRates", "local", "1.0.0")]
    [Description("Servidor x3: recoleccion y loot multiplicados, y el loot de los barriles va directo al inventario")]
    class ServerRates : RustPlugin
    {
        // ─────────────────────────────────────────────────────────────
        //  Configuracion (oxide/config/ServerRates.json)
        // ─────────────────────────────────────────────────────────────
        class Configuracion
        {
            public float Multiplicador = 3f;
            public bool LootDeBarrilesAlInventario = true;
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

        int Mult(int cantidad) => Math.Max(1, (int)Math.Round(cantidad * cfg.Multiplicador));

        void Mult(Item item)
        {
            if (item == null) return;
            item.amount = Mult(item.amount);
        }

        // ─────────────────────────────────────────────────────────────
        //  Recoleccion: arboles, piedras, animales, plantas, cantera...
        // ─────────────────────────────────────────────────────────────
        void OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item) => Mult(item);
        void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item) => Mult(item);
        void OnGrowableGathered(GrowableEntity planta, Item item, BasePlayer player) => Mult(item);
        void OnQuarryGather(MiningQuarry cantera, Item item) => Mult(item);
        void OnExcavatorGather(ExcavatorArm excavadora, Item item) => Mult(item);
        void OnSurveyGather(SurveyCharge carga, Item item) => Mult(item);

        // Lo que se recoge del suelo con E (piedras, madera, setas, canamo...)
        void OnCollectiblePickup(CollectibleEntity recogible, BasePlayer player, bool comer)
        {
            if (recogible?.itemList == null) return;
            foreach (var ia in recogible.itemList)
                ia.amount = Mult((int)ia.amount);
        }

        // ─────────────────────────────────────────────────────────────
        //  Loot: barriles, cajas...
        // ─────────────────────────────────────────────────────────────
        // OnLootSpawn salta justo antes de PopulateLoot, asi que se
        // multiplica en el siguiente tick, con la caja ya rellena.
        void OnLootSpawn(LootContainer caja)
        {
            NextTick(() =>
            {
                if (caja == null || caja.IsDestroyed || caja.inventory == null) return;
                foreach (var item in caja.inventory.itemList)
                {
                    if (item.info.stackable <= 1) continue;   // armas, ropa... no se multiplican
                    item.amount = Mult(item.amount);
                    item.MarkDirty();
                }
            });
        }

        // Al romper un barril el loot va directo al inventario en vez de caer
        // al suelo. OnEntityDeath salta antes de OnDied, que es donde se tira.
        void OnEntityDeath(LootContainer caja, HitInfo info)
        {
            if (!cfg.LootDeBarrilesAlInventario || caja?.inventory == null) return;
            var player = info?.InitiatorPlayer;
            if (player == null || player.IsNpc) return;

            foreach (var item in new List<Item>(caja.inventory.itemList))
            {
                item.RemoveFromContainer();
                player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            }
        }
    }
}
