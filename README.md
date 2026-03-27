GUI for ToonBoom Harmony .xscene and .psd files merger. 
<br />
The idea is to automate the merging of multiple `.xstage` scenes into one: <br />
- scan directories for file collection<br />
- export artists files to tpl folder<br />
- import all tpl files into target .xstage<br />
- import psd<br />
<br />
The current setup assumes a specific directory structure, but this can easily be customized by modifying the conditions in the `ProcessDirectories(string rootPath)` functions. The setup also uses 3 scripts located in the root of the project. RC_ImportTPL.js RC_ExportTPL.js RC_ImportPSD.js
<br />
BACKGROUNDS<br />
—EPISODE01<br />
——PSD_01<br />
——PSD_02<br />
——PSD_03<br />
—EPISODE02<br />
—EPISODE03<br />
<br />
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
<br />
All shots collected from EPISODE01 will be matched with all shots found in the ANIMATORS directory within EPISODE01. The program will then look into the parent BACKGROUNDS directory and match all `.psd` documents to the corresponding shots.




