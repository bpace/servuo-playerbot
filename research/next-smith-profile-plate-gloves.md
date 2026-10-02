# Next bounded smith profile slice: Plate Gloves with completed-product return parity

Checked 2026-10-01 against the current PlayerBots module, UO Offline commit `7f38c7cd586dc67dc96a4857754e65987351fe2e`, and ServUO pub57 source. No production code was changed.

## Decision

Add native `PlateGloves` as the next smith product, but ship it only with a small completed-product predicate repair: the labor-return predicates must recognize every already-deployed smith product (`Dagger`, `Cutlass`, `Broadsword`, `Scimitar`, and `PlateGorget`) plus `PlateGloves`. This is a modest extension of the real ore-to-Hawker-to-buyer loop, not a new commerce system.

## Why Plate Gloves

The current stock path already moves real mined `BaseOre` into marked Hawker workshop stock, gives that exact stack to a nearby authorized smith, smelts it through ServUO, and calls the native `DefBlacksmithy` `CraftItem` pipeline. [PlayerBotLabor.cs](../Scripts/Custom/PlayerBots/PlayerBotLabor.cs) selects the current profile at lines 314-331; [PlayerBotShop.cs](../Scripts/Custom/PlayerBots/PlayerBotShop.cs) owns the stock withdrawal at lines 116-145 and funded finished-good delivery at lines 414-444.

ServUO pub57 defines `PlateGloves` as a normal `BaseArmor` item, so the existing generic purchase-and-equip gate already applies: it only permits a `BaseArmor` purchase when that exact native layer is empty, then defers to `Mobile.EquipItem`. [PlateGloves.cs](../../../../upstream/ServUO-pub57/Scripts/Items/Equipment/Armor/PlateGloves.cs) and [PlayerBotShop.cs](../Scripts/Custom/PlayerBots/PlayerBotShop.cs) lines 293-306. Gloves add a distinct useful layer rather than another one-handed weapon competing with the existing weapon set.

The native recipe takes 12 `IronIngot`, needs 58.9--108.9 blacksmith skill, and has no expansion gate. [`DefBlacksmithy.cs:311-317`](../../../../upstream/ServUO-pub57/Scripts/Services/Craft/DefBlacksmithy.cs) The labor preparation floor is already 75.0 Blacksmith skill, so `CraftItem` retains the authoritative success/failure result. [PlayerBotLabor.cs](../Scripts/Custom/PlayerBots/PlayerBotLabor.cs) lines 137-150. `SBBlacksmith` already supplies the shard's exact values: 72 buyback and 155 retail. [`SBBlacksmith.cs:50,152`](../../../../upstream/ServUO-pub57/Scripts/VendorInfo/SBBlacksmith.cs)

UO Offline is relevant only as the parity target: its behavior registry includes a persistent `Crafter` role and its shop makes advertised goods real backpack inventory. [`BehaviorRegistry.cs:33-36`](../../../../upstream/uo-offline/playerbots/source/CustomBots/Behaviors/BehaviorRegistry.cs) [`BotShop.cs:2-10`](../../../../upstream/uo-offline/playerbots/source/CustomBots/BotShop.cs) The ServUO slice is deliberately narrower and stronger on provenance: no generated finished item, price, gold, or outcome.

## Required repair in the same slice

Do not add another item to `TryCraftBlacksmithProduct` alone. Its current selection already includes `Cutlass`, `Scimitar`, and `PlateGorget`, while `HasLaborGoods` and `DepositLaborGoods` only recognize `Dagger` and `Broadsword`. [PlayerBotLabor.cs](../Scripts/Custom/PlayerBots/PlayerBotLabor.cs) lines 314-331 and 515-548. A shift begins a Hawker return only when `HasLaborGoods` is true, so a smith that consumes its ingots and finishes only one of those three unrecognized goods can end with the product in its backpack rather than deliver it. Lines 442-490.

The Hawker side is already broader, recognizing all five deployed products in retail, delivered-good, wholesale, name, and price branches. [PlayerBotShop.cs](../Scripts/Custom/PlayerBots/PlayerBotShop.cs) lines 315-345 and 409-443. Bring the labor predicates to that same list before adding `PlateGloves` to both sides.

## Exact bounded implementation

Extend the existing >= 10-ingot profile selection so `PlateGloves` is eligible only with at least 12 actual iron ingots. Add its existing vendor values, `155` retail and `72` wholesale, to the already required `Price`, `StockName`, retail, delivered-good, and wholesale branches. Update labor's two return predicates to the complete six-product set. The current generic `BaseArmor` equipment path needs no special case.

Keep the existing gates unchanged: only locally authored Smithy sites, nearby live Hawker stock, physical container moves, native smelting/crafting, funded Hawker purchase, and vacant-layer `EquipItem`. Do not add bank withdrawals, recipe-specific success logic, price generation, item creation, replacement equipment, or new workshop types.

## Acceptance checks

With 12+ real iron ingots delivered by an existing Hawker, a smith can choose `PlateGloves`; ServUO's craft result is the only source of a finished item. A finished pair returns through the existing Hawker delivery, appears in WTS and the player purchase gump at 155 gold, pays 72 gold on funded delivery, and a local buyer equips it only when its Gloves layer is empty. Repeat those checks for Cutlass, Scimitar, and Plate Gorget while verifying the return-predicate repair.
