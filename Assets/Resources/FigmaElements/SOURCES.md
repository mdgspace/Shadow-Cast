# Figma foreground assets

Source: https://www.figma.com/design/n6UWgCq3r0xe5knCBHcUnJ/Syntax-AR-Dev?node-id=47-191

These PNGs are individual foreground layers exported from the supplied Menu Page, with their original pixels preserved. They are separate Unity Images and Buttons; no full-screen screenshot is displayed.

| Resource | Original exported layer |
| --- | --- |
| UmbraLogo | Image-3.png |
| StoryCard | Image.png |
| StoryIllustration | Image-2.png |
| StoryHeading | The Toybox.png (rendered Story mode heading) |
| StoryCaption | Explore objects.png (rendered Explore and beat levels caption) |
| QuickPlay | Groups-3.png |
| Arrow | Image-1.png |
| Settings | Image-5.png |
| Tagline | REAL OBJECTS.NEW.png |

`ShadowCastFigmaAssets.Illustration` also uses bounded illustration regions from the previously downloaded Chapter Page and Level Page exports as a texture atlas. Each region is a separate Sprite on its own card. Chapter titles, unlock state, and actions use live campaign data rather than the mock progress baked into those reference screens. Navigation, build, pause, settings, and result actions reuse the exported arrow icon with live labels. The room photograph is excluded from all UI surfaces.
