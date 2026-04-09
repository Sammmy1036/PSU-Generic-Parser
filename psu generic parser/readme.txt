# Sammmy1036
PSU Generic Parser Resurrected

Phantasy Star Universe houses game files inside the DATA folder. Any mods you create can be placed inside the Addon folder and will load into the game on launch. 

Clicking file > open > and selecting your DATA file will allow the parser to decrypt the hashed file and reveal its contents. 

Understanding the file types:
 ADX = Sound files (music, sound effects, voice)
 BIN = Text file / scripts (dialogue, menus, item descriptions, mission/enemy scripts and often paired with k files)
 DAT = Binary data file / container (archive / data blob and can house almost anything (settings, sounds, ui, packed assets etc) usually a pain 
 REL = Object layout / mission set files (used for placing objects, enemies, props, and triggers inside missions)
   K = Text / string files (Alternative extension for game text and localization (works like .bin for dialogue and UI strings)
 NOM = Animation files (player character animations)
 SFD = Video files (cutscenes and movies) often paired with ADX audio tracks. Try both also using .adx method for sound
 PSO = Compiled shader (Pixel Shader Object)
 XNA = Bones / skeleton name files (Lists bone names and skeleton data used by 3D models)
XNCP = UI / Control / Layout files (Used for HUD elements, menus, and UI layouts)
 XNJ = 3D model files (The main model format)
 XNM = Model material / mesh data (Usually paired with XNJ models; defines materials, meshes, and rendering properties) 
 XNR = Parameter / data table files (Contains item stats, weapon parameters, enemy drops, skills, technics, AI behavior, object definitions)
 XNT = Texture list / material mapping files (Links texture slots inside models (XNJ) to the actual files (XVR) A texture dictionary
 XVR = Texture files (Characters, environments, UI, etc)
 VSO = Compiled shader (Vertex Shader Object)

Some DATA files you try to open with the parser may not load anything at all. That will usually indicate that it is an ADX file. 
What you can try is renaming the hash with .adx at the end of it 

For example: 3a52ae80d8d308a604a8a70e207d9478.adx

Then trying to drop the file onto vgmstream-cli.exe. Which can be downloaded from https://vgmstream.org/ 
I use VGMStreams - Command-line, Winamp, XMPlay (but select whatever version you prefer)

After dropping your hash.adx file onto the vgmstream-cli.exe it will automatically convert it to a .wav file which can be previewed.

If you want to add your own custom .wav file into the game, I recommend ADX Converter & Player which can be downloaded from 
https://gamebanana.com/tools/6491

You can select ADXEncoder.exe > select your .wav > click adx format > it will output the ADX file

Once you have your won adx file you can rename it to the hash file you want to replace and remove the .adx file extension. 

Other things to note: 
Some audio is hidden behind .dat files. You may sometimes be able to preview the audio by importing it into audacity as raw data. 

# Agrathejagged 

FPB Extractor

Extracts archives from the Phantasy Star Portable series. Options are as follows:
* Rip files (no list): Goes through an FPB and extracts every identified AFS, mini AFS, NBL, and ADX (both streamed and sequenced). Does not identify files beyond type. THIS WILL LOSE PSP2/i'S PACK FILES (but the NBLs contained within will be extracted).
* Extract FPB using list: Uses a list file ripped from the game to properly extract all files in the FPB and, optionally, name them based on the crc32 of the filename. This .exe is shipped with the list data for PSP1 (NA/EUR/JP), PSP2 Demo (NA), PSP2 Localization Prototype (EUR), PSP2 (NA/EUR/JP), PSP2i Demo (JP), and PSP2i (JP). A list file will never be provided for the PSP2i translation because it's too much of a moving target.

NOTE:
media.fpb must first be decrypted externally. 

To do this for Infinity:
1. Download jPCSP. Note: At some point, this functionality appears to have gotten broken; I've had good luck with the 2015-03-20 18:05:10 build from https://buildbot.orphis.net/jpcsp/
2. In Options->Settings->Crypto, enable "Extract original PGD files to TMP folder"
3. Launch PSP2i, click "play".
4. media.fpb will be decrypted to your jPCSP folder, under tmp\NPJH50332\PGD\File-708640\PGDfile.raw.decrypted.


Custom list files:
FPB files have a mapping in the game to map the filename's CRC32 to the offset in file.fpb/media.fpb. To attempt to find one of these for an alternate version (such as a prerelease or a translation), take a memory dump of the game and try searching for one of these values:
PSP1 (release, full game): 
	51 63 CB 6A
	41 7B B3 D9
	62 88 08 A9

PSP2 (release, full game):
	8C 8B 06 3C
	2A A9 C7 C0
	B8 2A 5A FB

Once you find one of these, go back until you find what appears to be a count. 
- In PSP1 (NA/EUR), that count is DC090000, 
- In PSP2 (NA/EUR), it's F1120000, 
- In Infinity (final), it's 151A0000. 
Demos will have smaller counts, Japanese versions will probably have smaller counts.

Take that count, reverse the endian on it (so Infinity has 0x1A15 = 6,677 files), multiply by 8 (0x1A15 * 8 = D0A8), add 0x14 (0xD0A8 + 0x14 = 0xD0B4), and dump that many bytes STARTING WITH THE COUNT. This should be your file list.