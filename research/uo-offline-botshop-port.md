# UO Offline BotShop port boundary

Checked 2026-09-28 against local upstream commit `7f38c7cd586dc67dc96a4857754e65987351fe2e`.

UO Offline BotShop is real commerce, not vendor speech. `BotShop.cs:107-125,246-282,584-730` gives a hawker one actual backpack item, per-buyer haggling state, a time-bounded agreement, and removal of stock only when that real item leaves the seller. `BotShopDeal.cs:64-123,341-401` coordinates a buyer, affordability, travel, haggling, and settlement. `BotBanking.cs:59-149,162-238` uses actual banker-range deposit/withdrawal and never invents money. `BotBuyOffer.cs:28-40,177-280,641-680` extends the same contract to a real player selling to a bot through a real trade window.

The ServUO port currently has only shopper presence and a `Hawker` bank-sitter text role. A faithful port needs a new commerce module with four internal responsibilities: stock creation/ownership, buyer-specific offer state, bank-backed affordability, and atomic item/gold settlement. It must use native ServUO containers, `Banker` methods, and secure trade mechanics; it must never simulate success by directly minting gold or presenting stock that does not exist.

The first implementable foundation is passive real stock for Hawker fixtures, disabled by default until settlement is complete. It can create and retain a real item in a bot backpack and derive WTS speech from it, but it must not advertise or expose purchase paths until both settlement and player-trade validation exist. This does not yet close BotShop parity.
