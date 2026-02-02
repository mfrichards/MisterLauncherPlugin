# MisterLauncherPlugin
MisterLauncherPlugin is a LaunchBox plugin for launching games on MiSTer FPGA devices.
It essentially allows you to use your MiSTer FPGA as the emulator for many of the platforms
in your LaunchBox setup. 

## History
When I first discovered the MiSTer, I was intrigued by the accuracy and simplicity of FPGA based
emulators, and I wanted to use it as the main emulator in my arcade cabinet. However, the lack
of a graphical user interface with box art and game descriptions was a deal breaker as my arcade
cabinet is one of the central peices of my game room, and I wanted to retain that "wow" factor
when entertaining friends and family.

At length, I came across this great [guide by Point N' Click](https://github.com/PointNClickPops/MiSTerBox/wiki/GUIDE:-Launching-MiSTer-files-from-LaunchBox)
and the accompanying [YouTube video](https://www.youtube.com/watch?v=Wb5ReEOJI9c) where he
demonstrates using LaunchBox as a front end for the MiSTer. I wanted to expand upon this,
and accomplish a few additional goals:

1. Since I also have a number of platforms and games not supported by the MiSTer, I wanted the ability
to choose to launch a game with either regular software emulation, or with the MiSTer (if a game
is supported there).
1. I wanted to display LaunchBox (BigBox) and the MiSTer on the same screen and automatically
switch to the MiSTer when launching a game, so that it appears (mostly) seamless and looks just like
launching another emulator from within LaunchBox.
1. Instead of manually creating scripts for each game in your collection to use in LaunchBox,
I wanted the process be more automatic - just place the same rom files in LaunchBox and on the MiSTer,
and LaunchBox will automatically locate them and give you the option to launch the game on the MiSTer.

Like the guide referenced above, I rely heavily on [Wizzo's MiSTer Extensions](https://github.com/wizzomafizzo/mrext),
using the [Remote API](https://github.com/wizzomafizzo/mrext/blob/main/docs/remote.md) to search for and
launch games on the MiSTer. If a corresponding game exists on the MiSTer, this plugin adds menu items to
a game's context menu allowing you to launch that game (or one of it's alternate versions) on the MiSTer.

## Setup and Configuration

### Prerequisites

1. This guide assumes that you have setup your MiSTer device using the [Update All script](https://github.com/theypsilon/Update_All_MiSTer).
If you followed any of the common guides online for setting up the MiSTer, then you are probably familiar with
using this script to udpate your MiSTer. Specifically, you need to enable the `MiSTer Extensions` repository in the
in the Update All Unofficial Scripts menu to install [Wizzo's MiSTer Extensions](https://github.com/wizzomafizzo/mrext).
1. Launch the `Remote` script from the MiSTer `Scripts` menu, and set it to start on boot.
1. While not essential, it will be easier and more reliable if you set a static IP address on the MiSTer. Refer to the
[Advanced Networking](https://mister-devel.github.io/MkDocs_MiSTer/advanced/network/#network-access) section of the
MiSTer documentation for how to do this. The MiSTer and your LaunchBox installation must be on the same network
so the plugin can make calls to the Remote API on the MiSTer.
1. You should also allow the Update All script to download and update the Arcade cores and roms on the MiSTer. This
should be enabled by default and will happen the first time you run the script. MisterLauncherPlugin depends on the Arcade
cores and associated MRA files being installed to the default locations on the MiSTer (under the `_Arcade` directory).
Normally the arcade directory is found at `/media/fat/_Arcade`, however, if you store the arcade files in a different
location (like a USB drive or network share), you can configure the plugin to use that location instead
(see configuration settings below).
1. Place any console and computer rom files that you want to launch on the MiSTer in the appropriate directories on both
the MiSTer and LaunchBox. For example, to launch games for the Nintendo Entertainment System console on the MiSTer, place
a copy of your rom files in both the `\LaunchBox\Games\Nintendo Entertainment System` directory in LaunchBox, and the
`/media/fat/games/NES` directory on the MiSTer (or any alternate location you use for your MiSTer games, such as
`/media/usb0/games/NES` or `/media/fat/cifs/games/NES`).

### Plugin Installation

1. Download the latest release of MisterLauncherPlugin, and unzip its contents into the root folder of your Launchbox
installation. This should place these files in the following locations (verify this and copy the files manually
if necessary):
    * `MisterLauncherPlugin.dll` and `MisterLauncher.settings.json` in the `\LaunchBox\Plugins` directory.
    * `aracde.db` in the `\LaunchBox\Metadata` directory.
    * `build_db.py` and `relay.py` in the `\LaunchBox\Scripts` directory. Note these are optional utility scripts,
    and can actually be placed anywhere (see below for usage details).
2. Open `MisterLauncher.settings.json` in notepad (or any text editor) and customize the following settings:
    *  `Mister > ApiURL` - Set this to point to the IP address of your MiSTer device (if you set a static IP address).
    If you do not use a static IP, the default value `http://mister:8182` should work, but may be less reliable.
    * `Mister > ArcadePath` - Set this to the location of the `_Arcade` folder on your MiSTer. If you store your
    rom files on the sdcard of the MiSTer (the default), then the default value of `/media/fat` will work. However,
    if your `_Arcade` directory is on an external drive or network share, you will need to change this to `/media/usb0`
    or `/media/fat/cifs`.
    * Under the `Consoles` section list all of the consoles that contain rom files in LaunchBox and on the MiSTer that
    you want to launch with the plugin. The format of each entry is `"<LaunchBox folder>:<MiSTer folder>"` (for example
    `"Nintendo Entertainment System:NES"` for the NES console). A list of MiSTer cores and their corresponding rom
    folders can be found in the [MiSTer documentation](https://mister-devel.github.io/MkDocs_MiSTer/cores/console/),
    although that list may be out-of-date.
    * Do the same under the `Computers` section for computer platforms you wish to use in both LaunchBox and the MiSTer
    (for example `"Commodore 64:C64"`). The only difference between platforms listed here rather than under `Consoles`
    is that the media type of a rom file will be appended to the title, as computers often use several different types
    of media such as cart, disk, or tape.
    * There are other settings that can be customized - see below for more details.
 
 ## Operation

 1. Make sure that your MiSTer is on and connected to the network.
 1. For the plugin to operate correctly, you must initiate an `index` operation using the remote API on the MiSTer:
    * `curl -X POST 'http://mister:8182/api/games/index'`
    * Or in Windows PowerShell: `Invoke-RestMethod -Uri http://mister:8182/api/games/index -Method POST`
    * You only need to do this once initially, and then again whenever you add or remove files in your rom folders.

3. Now open LaunchBox (or BigBox), and when you select a game (right click on a game in LaunchBox, or go to the
detail page for a game in BigBox), if that game is also found on the MiSTer, you should see a menu item at the bottom of the
menu to `Launch <game version> on MiSTer...`. If multiple versions of the game are found on the MiSTer, there will
be multiple menu items.

## Advanced Configuration

TODO