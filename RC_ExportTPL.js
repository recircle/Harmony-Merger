/*
export .tpl to a harmony library
*/

function exportSelectionAsTemplate() {
    var config = {
        scenePath: "",
        tplName: "",
        targetPath: "",
        compositeNode: "Top/Composite",
        excludeList: ["TopLayer", "V1", "Camera", "Camera_Peg", "Colour-Card"]
    };

    // --- BLOCK 1: PATH & FILENAME LOGIC ---
    try {
        var scenePath = scene.currentProjectPath().replace(/\\/g, "/");
        var pathParts = scenePath.split("/");
        var parentDir = pathParts[pathParts.length - 2];

        if (parentDir === "" || parentDir.indexOf(".xstage") !== -1) {
            parentDir = pathParts[pathParts.length - 3];
        }

        var sceneFileName = scene.currentScene();
        // Sanitize name: remove spaces/special chars that cause permission errors
        config.tplName = (parentDir + "_" + sceneFileName).replace(/[^a-z0-9._-]/gi, '_') + ".tpl";

        //var libPath = "D:/MERGE_TEST/IMPORT_LIBRARY/EP01";
        var libPath = System.getenv("MY_LIB_PATH");
        if (!libPath) throw new Error("Environment Variable 'MY_LIB_PATH' is missing.");

        config.targetPath = libPath.replace(/\\/g, "/");
        if (config.targetPath.slice(-1) !== "/") config.targetPath += "/";

        MessageLog.trace("Step 1 Success: Paths initialized for " + config.tplName);
    } catch (e) {
        MessageLog.trace("Step 1 Failure (Paths): " + e.message);
        return;
    }

    // --- BLOCK 2: NODE SELECTION ---
    try {
        if (!config.compositeNode) {
            throw new Error("Required node " + config.compositeNode + " not found.");
        }

        selection.clearSelection();
        var nodesToSelect = [];
        findUpperNodes(config.compositeNode, nodesToSelect);

        for (var i = 0; i < nodesToSelect.length; i++) {
            var nodeName = node.getName(nodesToSelect[i]);
            if (config.excludeList.indexOf(nodeName) === -1) {
                selection.addNodeToSelection(nodesToSelect[i]);
            }
        }
        selection.addNodeToSelection(config.compositeNode);

        MessageLog.trace("Step 2 Success: Nodes gathered and selected.");
    } catch (e) {
        MessageLog.trace("Step 2 Failure (Selection): " + e.message);
        return;
    }

    // --- BLOCK 3: FOLDER PREP & SAVE ---
    try {
        var dir = new Dir(config.targetPath);
        if (!dir.exists) {
            if (!dir.mkdirs()) throw new Error("OS denied folder creation at " + config.targetPath);
        }

        var maxRetries = 3;
        var resultPath = "";

        for (var r = 0; r < maxRetries; r++) {
            resultPath = copyPaste.createTemplateFromSelection(config.tplName, config.targetPath);
            if (resultPath !== "") break; // Success!

            MessageLog.trace("Retry " + (r + 1) + ": Folder busy, waiting...");
            // There is no Sleep in Harmony JS, so we just log and try again
        }

        if (!resultPath || resultPath === "") {
            throw new Error("Template creation failed. Folder may be 'In Use' or Read-Only.");
        }

        MessageLog.trace("Step 3 Success: Template exported to " + resultPath);
    } catch (e) {
        MessageLog.trace("Step 3 Failure (Save): " + e.message);
    }
}

function findUpperNodes(targetNode, nodeList) {
    var numInputs = node.numberOfInputPorts(targetNode);
    for (var i = 0; i < numInputs; i++) {
        var sourceNode = node.srcNode(targetNode, i);
        if (sourceNode && nodeList.indexOf(sourceNode) === -1) {
            nodeList.push(sourceNode);
            findUpperNodes(sourceNode, nodeList);
        }
    }
}

exportSelectionAsTemplate();
