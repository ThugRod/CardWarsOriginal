# Card Wars
(Modern Android build: Unity 2022.3.55f1, IL2CPP, and ARM64.)

Changes:
- Updated the project to work with Unity 2022.3.55f1
- Android builds use IL2CPP and ARM64 for modern 64-bit-only devices
- Included an all-content-unlocked mod with infinite coins
- Fixed multiple shader errors and network errors
- Fixed deprecated code with updated Unity methods

## Android ARM64 build

Open the project with Unity 2022.3.55f1 and install Android Build Support, Android SDK & NDK Tools, and OpenJDK from Unity Hub. Build the Android player normally; the project is configured for IL2CPP and ARM64 (`arm64-v8a`). The minimum Android API remains 22 and the target API uses the highest SDK installed with Unity.

## Included mod

`Assets/Scripts/Assembly-CSharp/CardWarsMod.cs` enables all cards, leaders, quests, regions, dungeons, and skips tutorial locks whenever a profile is loaded. Coins are held at a minimum balance of 1,000,000,000, so purchases work normally without consuming the balance. Both features can be disabled through the constants in `CardWarsModSettings`.

Issues:
From what I've seen there's an issue on the Fionna and Cake level 24, I want to look into it and attempt a fix ASAP.
There's no ETA on when I'll be able to thoroughly look through the game as I'm currently trying to fix the PvP and Unity Project for Card Wars Kingdom.

A port of the "Adventure Time: Card Wars" mobile game to PC and updated for ANDROID

Floop the Pig! It's Adventure Time CARD WARS! Play the game inspired by the Adventure TIme episode, 'Card Wars'! Summon creatures and cast spells to battle your way to victory.


CARD COMBAT!

Command an army of awesome warriors, including Husker Knights, Cool Dog the Immortal Maize Walker, and even the Pig to destroy your opponent's forces! Place towers and cast spells to unleash ultimo attacks

CUSTOM DECKS!

Collect new cards to customize your deck for each opponent. Level up your creatures, spells, and towers, or fuse them together to make your cards even more powerful.

HIGH STAKES BATTLES!

Think you've got what it takes to be crowned a Cool Guy, or will you end up drinking from the Dweeb cup? Play as Finn Jake, BMO, Princess Bubblegum, Marceline, Flame Princess and more as you wind your way through the Land of Ooo!

It's CARD WARS! 

## Download

* [Latest Windows Version](https://github.com/ThugRod/CardWarsOriginal/releases/download/latest/CardWars-Windows.zip)
* [Latest Android Version](https://github.com/ThugRod/CardWarsOriginal/releases/download/latest/CardWars.apk)
* IOS & Linux - Hopefully Coming Soon

## Images
![CardWars_r8d9H393Tp](https://i.imgur.com/cXUolY0.jpg)
![CardWars_F7nDRbIxel](https://i.imgur.com/N3BH326.jpg)

## Contributing
Card Wars used Unity 2017.4.40f1. Other dependencies may be required.
Imported to Unity 2022.3.55f1 for Android ARM64 and 16 KB memory-page compatibility
