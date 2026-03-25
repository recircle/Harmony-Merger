# Toonboom Harmony Merger
Toonboom Harmony Scene Merge Manager

GUI for ToonBoom Harmony .xscene merger. 

The idea is to automate the merging of multiple `.xstage` scenes into one, and finally add a background.

The current setup assumes a specific directory structure, but it can easily be customized by modifying the conditions in the `ProcessDirectories(string rootPath)` function. 

BACKGROUNDS
—EPISODE01
—EPISODE02
—EPISODE03

ANIMATION
—EPISODE01
——ANIMATORS
———ARTIST01
————SHOT01
————SHOT02
————SHOT03
———ARTIST02
————SHOT01
————SHOT02
————SHOT03
——SHOT01
——SHOT02
——SHOT03

—EPISODE02
——SHOT01
——SHOT02
——SHOT03

All shots collected from EPISODE01 will be matched with all shots found in the ANIMATORS directory within EPISODE01. The program will then look into the parent BACKGROUNDS directory and match all `.psd` documents to the corresponding shots.




