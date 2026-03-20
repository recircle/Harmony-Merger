/*
import .tpl in a harmony scene
*/



function importTemplatesToGroups() {
    var libPath = System.getenv("MY_LIB_PATH");
    //var libPath = "D:/MERGE_TEST/IMPORT_LIBRARY/EP01";

    var sceneFileName = scene.currentScene();
    var sceneSuffix = sceneFileName.substring(sceneFileName.length - 3);
    var mainComposite = "Top/Composite";
    var tplFiles = [];

    // --- BLOCK 1: GET FILES (Discovery) ---
    try {
        if (!libPath) throw new Error("MY_LIB_PATH environment variable is not set.");

        var importFolder = libPath.replace(/\\/g, "/");
        var dir = new Dir(importFolder);

        if (!dir.exists) throw new Error("Library folder not found at: " + importFolder);

        var allTpls = dir.entryList("*.tpl");
        for (var i = 0; i < allTpls.length; i++) {
            var tplBase = allTpls[i].replace(".tpl", "");
            if (tplBase.substring(tplBase.length - 3) === sceneSuffix) {
                tplFiles.push({
                    name: tplBase,
                    path: importFolder + "/" + allTpls[i]
                });
                MessageLog.trace("Step 0: Found .tpl file: " + tplBase);
            }
        }

        if (tplFiles.length === 0) {
            MessageLog.trace("Step 1: No matching .tpl files found for suffix: " + sceneSuffix);
            return;
        }
        MessageLog.trace("Step 1 Success: Found " + tplFiles.length + " matching templates.");
    } catch (e) {
        MessageLog.trace("Step 1 Failure (Discovery): " + e.message);
        return;
    }

    // --- LOOP THROUGH MATCHES ---
    for (var j = 0; j < tplFiles.length; j++) {
        var currentTpl = tplFiles[j];
        var newGroupPath = "";

        // --- BLOCK 2: ADD TO GROUP (Container Setup) ---
        try {
            var groupName = "GRP_" + currentTpl.name;
            newGroupPath = node.add("Top", groupName, "GROUP", 0, 0, 0);

            if (!newGroupPath) throw new Error("Could not create group node: " + groupName);

            // Setup Multi-Ports inside the new group
            node.add(newGroupPath, "Multi-Port_In", "MULTIPORT_IN", 0, -500, 0);
            node.add(newGroupPath, "Multi-Port_Out", "MULTIPORT_OUT", 0, 0, 0);

            MessageLog.trace("Step 2 Success: Created container " + groupName);
        } catch (e) {
            MessageLog.trace("Step 2 Failure (Group Setup): " + e.message);
            continue; // Skip to next template
        }

        // --- BLOCK 3: ADD TO SCENE (Pasting & Linking) ---
        try {
            var pOptions = copyPaste.getCurrentPasteOptions();
            pOptions.lockAggregation = false;
            pOptions.allFromColumn = false; // Force unique drawings

            var success = copyPaste.pasteTemplateIntoGroup(currentTpl.path, newGroupPath, 1, pOptions);
            if (!success) throw new Error("Paste failed. .tpl might be corrupt or in-use.");

            // Internal Linking (Composite to Multi-Port Out)
            var subNodes = node.subNodes(newGroupPath);
            var mIn = newGroupPath + "/Multi-Port_In";
            var mOut = newGroupPath + "/Multi-Port_Out";

            for (var n = 0; n < subNodes.length; n++) {
                var currentNode = subNodes[n];
                var nodeName = node.getName(currentNode);
                if (nodeName === "Composite") {
                    node.link(currentNode, 0, mOut, 0);
                    break;
                }
            }

            //for (var n = 0; n < subNodes.length; n++) {
            //    var currentNode = subNodes[n];
            //    if (node.type(subNodes[n]) === "PEG") {
            //        var numInputs = node.numberOfInputPorts(currentNode);
            //        var isConnected = false;

            //        for (var p = 0; p < numInputs; p++) {
            //            if (node.isLinked(currentNode, p)) {
            //                isConnected = true;
            //                break;
            //            }
            //        }

            //        if (!isConnected) {
            //            node.link(subNodes[n], 0, mIn, 0, false, false);
            //            portIndex++; // Increment to create a new port on Multi-Port In
            //        }
            //    }
            //}

            node.link(newGroupPath, 0, mainComposite, 0);
            node.setCoord(newGroupPath, j * 250, -200);

            MessageLog.trace("Step 3 Success: Template " + currentTpl.name + " integrated.");
        } catch (e) {
            MessageLog.trace("Step 3 Failure (Injection): " + e.message);
        }
    }
}

importTemplatesToGroups();




