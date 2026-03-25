# Toonboom Harmony Merger
Toonboom Harmony Scene Merge Manager

GUI for ToonBoom Harmony .xscene merger. 

The idea is to automate the merging of multiple `.xstage` scenes into one, and finally add a background.
<br />
The current setup assumes a specific directory structure, but it can easily be customized by modifying the conditions in the `ProcessDirectories(string rootPath)` function. 
<br />

BACKGROUNDS<br />
—EPISODE01<br />
—EPISODE02<br />
—EPISODE03<br />

ANIMATION<br />
—EPISODE01<br />
——ANIMATORS<br />
———ARTIST01<br />
————SHOT01<br />
————SHOT02<br />
————SHOT03<br />
———ARTIST02<br />
————SHOT01<br />
————SHOT02<br />
————SHOT03<br />
——SHOT01<br />
——SHOT02<br />
——SHOT03<br />
<br />
—EPISODE02<br />
——SHOT01<br />
——SHOT02<br />
——SHOT03<br />

All shots collected from EPISODE01 will be matched with all shots found in the ANIMATORS directory within EPISODE01. The program will then look into the parent BACKGROUNDS directory and match all `.psd` documents to the corresponding shots.




