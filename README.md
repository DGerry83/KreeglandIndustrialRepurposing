![alt text](https://i.imgur.com/IreNucx.png "Don't pay more, pay Kreegland.")
# Kreegland Industrial Repurposing
A Kerbal Space Program Mod
# About Us

Kreegland's Industrial Repurposing division focuses on breathing new life into old parts.  Though our competitors may describe our equipment as "junk" or "that garbage we threw away years ago" or even "dangerous and unfit for purpose", we think you'll agree there's no cheaper part than ours.  At Kreegland, our Industrial Repurposing division understands the budget demands of modern space programs.  We know that money absolutely is an object, and we can help you spend as little of it as possible.


## What does this mod do?
Great question!  The Industrial Repurposing division is currently hard at work designing new materials and textures for some of their favourite old parts from mods like Umbra Space Industries and Global Construction.  Each part requires a special level of care that only the most skilled engineers in the Repurposing division can provide.  The structure is inspected and repaired, the paint is painstakingly restored, and most importantly the internals are carefully removed and replaced with updated versions.  If someone told you we carelessly rip things out and leave them in the lake behind our facility, we'd like to know who that was so we can discuss the matter further.  We don't do that.

## What does it really do?
Okay seriously what it does is primarily update the materials on old parts as mentioned using Textures Unlimited/Shabby(dependencies of this mod).  Some of the workings have been replaced to update from FireSpitter modules to B9Part Switch modules, and the parts all assume you will be using TweakScale to scale them up or down as needed so only one size variant is included.  All parts listed currently require the original mod to be installed regardless of whether the configs included in the mod update an existing part or create a new part using the old part model.  All art assets from these mods are All Rights Reserved as as a result I cannot distribute them without permission.  Installing the mods normally is enough, as the original/default install location is referenced when specifying models to use.  Currently the mod includes the following updated parts:

### Umbra Space Industries
1. Kontainer - resource storage variants
2. Kontainer - Kerbal Inventory version
More to come...

### Screenshots
![alt text](https://github.com/DGerry83/KreeglandIndustrialRepurposing/blob/main/Screenshots/KontainerScreen1.png "Great Wall of Kontainers")
![alt text](https://github.com/DGerry83/KreeglandIndustrialRepurposing/blob/main/Screenshots/KontainerScreen2.png "Kontainer Assortment")
![alt text](https://github.com/DGerry83/KreeglandIndustrialRepurposing/blob/main/Screenshots/KontainerScreen3.png "Material Kits Kontainer")


## More info...
#### How big are these materials?
Currently the Kontainers are all 4k resolution, with each material requiring four maps - each USI Kontainer only requires one unique map, the diffuse map.  The metalness/roughness map, the normal map, and the mask are all re-used.  Currently the total size on disk for all four maps is 64MB - 21.3MB each for the normal and Metal/roughness maps, and 10.6MB each for the diffuse and mask maps.  All maps are the same resolution, but the diffuse and mask maps can be compressed more effectively.  Re-doing the maps at any particular resolution, especially lower, is not difficult and something I may consider if enough people have issues with the mod.  I don't run a huge number of part and graphics mods myself, so it's harder for me to evaluate the impact of this mod with respect to an already-full install.  A 2k version would be easy to make though, so let me know if you feel the memory footprint of this mod is(or becomes) too large for you.  Future materials will be given sizes appropriate to their expected part size, and as the plan for these Kontainers was for them to be on the large side I thought a larger map was appropriate.

####  What's that bridge module?
The TexturesUnlimitedB9Bridge.dll file includes a small companion document explaining its function, but essentially it triggers Textures Unlimited material updates on certain conditions that allows changes to currentTextureSet via B9PartSwitch to update immediately in the scene.  The bridge doesn't specifically "listen" for B9 in any way, but interoperability with B9 was the intent - I believe it would work with other switchers as it should work with any PAW tweaking that changes currentTextureSet.  It's very small, and the source code is available if you'd like to compile it yourself.  I am NOT a coder, just an artist, but I felt this was necessary to allow players to have the materials update automatically and immediately.  I don't think this will cause any problems, it doesn't do very much, but if you think it needs to be changed or fixed don't hesitate to tell me because you probably know more than I do.

#### What does "reset texture" do?  Why is it there?
"Reset Texture" calls on Textures Unlimited to update the material on the part, and it does so in a way that will reset the color data to the presets in the currently-selected texture set.  In the editor this happens automatically when you swap textures or when B9PartSwitch does it for you by swapping part subtypes.  In flight, however, only the textures themselves will be updated when you change B9 subtypes.  This is intentional, as it allows you to swap the B9 Subtype but keep the current color information - so if you had set up a custom colorway in the editor, you'll keep it when you swap tanks in flight.  If you want to reset the colors back to the default for the current texture set in flight, simply use the "reset texture" button.  If your textures otherwise aren't changing in some situation when you think they should, try that button - but also report an issue.

#### My materials don't look like the screenshots...
As of writing(December 8, 2025) Textures Unlimited contains a very minor bug that prevents it from loading the "detail" slider value from color presets.  The change that needs to happen is in IRecolorable.cs on line 177.  "detail = 1;" needs to be replaced with "detail = preset.detail;".  Again, I'm an artist not a coder, but this seemed like the right fix for the problem and it also does solve the problem.  I am not, however, distributing an updated version of the dll.  If you want to make the change and compile it yourself it's fairly straightforward, but I hope this section will be removed from the readme soon because I hope TU incorporates the fix soon.

## 🛠️ Manual Installation

1. Download `KreeglandIndustrialRepurposing.zip`
2. Extract it to your KSP GameData folder
3. Enjoy!


## ⚠️ Known Issue - Textures Unlimited Bug

The current public version of Textures Unlimited has a bug where it doesn't use 
the `detail` field from texture set definitions. This affects material appearance.

**Fix submitted:** I've reported this to the TU team with the one-line code fix:
`detail = 1;` → `detail = preset.detail;` on line 177 of IRecolorable.cs

**Workaround:** Compile TU from source with the fix, or wait for next TU release.

**Status:** https://github.com/KSPModStewards/TexturesUnlimited/issues/7

### 📦 Dependencies

**REQUIRED MODS:**
| **USI Core** | Kontainers system | [GitHub](https://github.com/UmbraSpaceIndustries/USI_Core)
| **B9 Part Switch** | Part switching | [GitHub](https://github.com/KSPModStewards/B9PartSwitch)
| **Textures Unlimited** | Texture support | [GitHub](https://github.com/KSPModStewards/TexturesUnlimited)
| **ModuleManager** | Config patching | [GitHub](https://github.com/sarbian/ModuleManager)
| **Community Resource Pack** | Standard resources | [GitHub](https://github.com/UmbraSpaceIndustries/CommunityResourcePack)

### 🤝 For Other Modders

TexutresUnlimitedB9Bridge.dll(and its source) is available for other mods/modders to use!
It provides immediate TU material updates when changing `currentTextureSet` via PAW action in flight and in editor.

License: [CC0]

### Remaining licenses

- **Art (.dds files):** All Rights Reserved
- **Configs & code:** CC BY-SA 4.0
