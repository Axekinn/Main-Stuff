<a style="text-decoration:none" href="https://github.com/Koriebonx98/Main-Stuff/raw/main/bin/publish/setup.exe">
    <img src="https://img.shields.io/badge/Download%20Installer-blue.svg?style=flat-round" alt="Download link" />
</a>

# Main-Stuff
mix of pc stuff

#includes
Playnite Addons:

Local PC Game Importer:
</a>

#Info:
</a>
Make Folders on all drrives wo use "Games" "Repacks". e.g "D:\Games" "E:\Repacks" 

</a>
Any Game in "Games" gets added as "isntalled" and all exe's get addeda as playactions

</a>
any Repack in "Repacks" gets added as uninstalled but makes a "Install" action for the "Setup.exe" file in folder 

</a>
if in both "Repacks" & "Games" it gets added as Installed and adds a "Install" action

</a>
if Game in "Games" has an "Unisntall.exe" itll make a "unsinstall" Action as well

</a>
if game was uninstalled and then moved to a diffrent drive, itll update InstallDir

</a>

Uses:
</a>

Portable drive with Repacks or a SMB with Network drives set up also work fine. can also play games via smb but can cause slower boot ups/input delay (possible and worked well for me)
</a>

Keeps it easy to see what games are avalible localy 
</a>


</a>
still work in progress

</a>
Future:
</a>

Exclusion list 
</a>

using the real "Install" & "Uninstall" buttons
</a>

Mangement "copy" & "Move" to a diffrent drive that has enough storage space 


# Auto Rom:
</a>
Not Released as of yet as im trying to sort some bugs but addon is 90% ready for first release

Info:
</a>

- Scrapes urls for each platfomr (Option in .config) if more than 1x region it adds aall to game entry, same for "Addon" or "Dlc" 
</a>

- Imports local roms and sets up with certain emus... (In Future will be user choice// Setting in .config to enable or disable this feature)
</a>

- HD Texture Packs: uses github .txt file (Cominity to add to this) for matching game in playnite, it then addds action to obtain texture pack, also adds "HD Texture Pack" Feature to games that have them, as list is online via github this will update for everyone, ive been making some simple menu/text sharpness packs for pcsx2 (disclusore, if any copyright infringment... contact me the platform, name of game, author and link and i will be sure to remove, if users keep readding "Banned" links then i will make list Closed source and only add those that are verified as fine.. again if contacted about copyright i will remove)
</a>

- Nintendo Switch Achivements: As of now it makes a .txt with the switch games in it, and say "True" if w.i.p ach on my github. over time itll then get the .json file and use with success sstory & Ach unlock script (Not all Games will work) also settings to enable/ disable this are in the .config
</a>


To come...
</a>

- Xbox 360 Achivements, when adding xbox 360 games to check success story if game has .json file else obtain via github and force success story to use for xbox 360 games (use efman version to have working xbox 360 games in success story)
</a>

- ENg Only: This will only get Games if Regions "World" or "USA" else itll look for "En" in the lang section of game name from url.. this should make it easier to get eng only Games
</a>

- Console Exclusives: This will add a Feature "Exclusive" by reading .txt on github and if game match itll add feature to game, this is nicer to know what was console exclusives, there will be settings for this so like when url scraping can have a setting to only scrape for exclusive games!
 </a>
