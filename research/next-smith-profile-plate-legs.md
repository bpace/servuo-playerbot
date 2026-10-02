# Next bounded smith profile slice: Plate Legs

Checked 2026-10-02 against the deployed Plate Arms profile, current PlayerBots, and ServUO pub57 source. This is research only. No runtime code or deployment changed.

## Decision

Add native `PlateLegs` next. It is the best remaining single-product increment: a distinct legs layer, a native recipe already below the labor skill floor, and established `SBBlacksmith` prices that are materially useful in the existing Hawker market.

## Candidate comparison

`PlateLegs` is the recommended product. ServUO defines it as 20 `IronIngot` at 68.8--118.8 Blacksmithy, so the current 75.0 labor floor is already eligible to attempt it through the native `CraftItem` pipeline. Its vendor data is 109 buyback and 218 retail. [`DefBlacksmithy.cs:311-317`](../../../../upstream/ServUO-pub57/Scripts/Services/Craft/DefBlacksmithy.cs) [`SBBlacksmith.cs:46-52,150-155`](../../../../upstream/ServUO-pub57/Scripts/VendorInfo/SBBlacksmith.cs) [`PlayerBotLabor.cs:310-345`](../Scripts/Custom/PlayerBots/PlayerBotLabor.cs)

`PlateChest` is native and exactly meets the 75.0 floor, but needs 25 ingots. It sells for 121 buyback / 243 retail, making it a larger stock threshold for only a modest price increase over Legs. Keep it as the following plate slice. [`DefBlacksmithy.cs:315-317`](../../../../upstream/ServUO-pub57/Scripts/Services/Craft/DefBlacksmithy.cs) [`SBBlacksmith.cs:46-49,150-154`](../../../../upstream/ServUO-pub57/Scripts/VendorInfo/SBBlacksmith.cs)

`PlateHelm` is a native 15-ingot recipe at 62.6--112.6 Blacksmithy, but its same vendor data values it at only 10 buyback / 21 retail. It does add a Helm-layer item, but is a poor next commerce good: it consumes substantial real ore while adding almost no funded Hawker turnover. [`DefBlacksmithy.cs:371-376`](../../../../upstream/ServUO-pub57/Scripts/Services/Craft/DefBlacksmithy.cs) [`SBBlacksmith.cs:52,60,166-168`](../../../../upstream/ServUO-pub57/Scripts/VendorInfo/SBBlacksmith.cs)

All three are normal `BaseArmor` types. The current buyer path admits a purchased armor item only when its exact native `stock.Layer` is vacant, then calls `EquipItem`; Plate Legs therefore needs no legs-specific equipment branch. [`PlateLegs.cs:6-19`](../../../../upstream/ServUO-pub57/Scripts/Items/Equipment/Armor/PlateLegs.cs) [`PlayerBotShop.cs:293-306`](../Scripts/Custom/PlayerBots/PlayerBotShop.cs) UO Offline's full-plate setup also includes Plate Legs, but that is parity context rather than an API or implementation source. [`EquipmentTable.cs:1528-1540`](../../../../upstream/uo-offline/playerbots/source/CustomBots/EquipmentTable.cs)

## Exact bounded change

At 20 or more actual iron ingots, add `PlateLegs` as one additional native `DefBlacksmithy` selection. Add it to the existing blacksmith return, retail, delivered-good, `Price`, `StockName`, and wholesale branches using 218 retail and 109 buyback. Leave the generic `BaseArmor` purchase/equip logic untouched.

Keep the existing local Smithy, marked Hawker ore, physical transfer, native smelting/crafting, funded sale, and vacant-layer gates. Do not create an item, price, gold, or craft result; do not add a separate equipment rule.

## Acceptance checks

With 20+ real iron ingots at an eligible Smithy, a smith can select `PlateLegs` and ServUO determines the outcome. A completed pair returns to Hawker, lists and sells for 218 gold, pays 109 gold on funded delivery, and equips only when the buyer's native Legs layer is empty.
