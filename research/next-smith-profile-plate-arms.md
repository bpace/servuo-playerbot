# Next bounded smith profile slice: Plate Arms

Checked 2026-10-02 against current PlayerBots, ServUO pub57, and UO Offline commit `7f38c7cd586dc67dc96a4857754e65987351fe2e`. This is research only. No production code or deployment changed.

## Decision

Add native `PlateArms` as the next single smith product. It is the smallest remaining classic plate recipe that extends the deployed set into a distinct armor slot without adding a new workshop, material, pricing rule, commerce flow, or equipment special case.

## Source-backed fit

ServUO pub57 registers `PlateArms` in `DefBlacksmithy` with 18 `IronIngot` and a 66.3--116.3 Blacksmithy range. That is compatible with the current 75.0 Blacksmithy labor floor and real-ore native `CraftItem` route. [`DefBlacksmithy.cs:311-317`](../../../../upstream/ServUO-pub57/Scripts/Services/Craft/DefBlacksmithy.cs) [`PlayerBotLabor.cs:314-350`](../Scripts/Custom/PlayerBots/PlayerBotLabor.cs)

`PlateArms` is a normal `BaseArmor` item, and the item constructor uses the native `0x1410` art. [`PlateArms.cs:6-19`](../../../../upstream/ServUO-pub57/Scripts/Items/Equipment/Armor/PlateArms.cs) The existing buyer check already uses the produced item's actual `Layer`, requires that layer to be empty, and delegates the equip decision to native `EquipItem`; no arms-specific code is needed. [`PlayerBotShop.cs:293-306`](../Scripts/Custom/PlayerBots/PlayerBotShop.cs)

`SBBlacksmith` supplies both shard values: 94 buyback and 188 retail. [`SBBlacksmith.cs:46-52,150-155`](../../../../upstream/ServUO-pub57/Scripts/VendorInfo/SBBlacksmith.cs) The existing Hawker branches follow this same wholesale/retail split for the deployed smith goods. [`PlayerBotShop.cs:315-345,411-444`](../Scripts/Custom/PlayerBots/PlayerBotShop.cs)

Compared with the adjacent classic plate candidates, Arms is the bounded next increment: Legs needs 20 ingots and 68.8 skill, while Chest needs 25 ingots and 75.0 skill. [`DefBlacksmithy.cs:311-317`](../../../../upstream/ServUO-pub57/Scripts/Services/Craft/DefBlacksmithy.cs) UO Offline is parity context only: its plate equipment table equips `PlateArms` alongside the currently deployed gorget and gloves, while its blacksmith production profile jumps from gloves to chest. [`EquipmentTable.cs:1528-1538`](../../../../upstream/uo-offline/playerbots/source/CustomBots/EquipmentTable.cs) [`CrafterProfiles.cs:108-126`](../../../../upstream/uo-offline/playerbots/source/CustomBots/Behaviors/CrafterProfiles.cs)

## Exact bounded change

At 18 or more actual iron ingots, allow `PlateArms` as one additional native `DefBlacksmithy` selection. Add it to the existing blacksmith return, retail, delivered-good, `Price`, `StockName`, and wholesale branches using 188 retail and 94 buyback. The generic `BaseArmor` purchase/equip path stays unchanged.

Keep all current constraints: local authored Smithy, marked nearby Hawker ore, physical container moves, native smelting and crafting, funded Hawker delivery, and vacant-layer `EquipItem`. Do not generate an item, gold, price, or craft result; do not add bank withdrawal, recipe-specific success logic, or an arms-specific equip path.

## Acceptance checks

With 18+ real iron ingots at an eligible Smithy, a smith can choose `PlateArms` and ServUO alone decides success. A completed pair returns to a Hawker, lists and sells for 188 gold, pays 94 gold on funded delivery, and equips only if the buyer's native Arms layer is vacant.
