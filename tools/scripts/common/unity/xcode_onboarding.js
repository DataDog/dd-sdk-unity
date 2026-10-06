// Unless explicitly stated otherwise, licensed under Apache License Version 2.0.
// Copyright 2026-Present Datadog, Inc.
//
// Runs only in the CI VM. Targets one PID; never activates or opens an application.
function readProperty(element, key, fallback) {
    try { return element[key](); } catch (_) { return fallback; }
}

function inspectWindow(window) {
    var pending = [{element: window, depth: 0}], controls = [], depthTruncated = false;
    while (pending.length && controls.length < 500) {
        var item = pending.shift(), element = item.element;
        var role = readProperty(element, "role", "");
        var value = role.indexOf("Secure") >= 0 ? "[redacted]" : readProperty(element, "value", null);
        if (typeof value === "string") value = value.slice(0, 300);
        if (value !== null && typeof value !== "string" && typeof value !== "number" && typeof value !== "boolean") value = null;
        controls.push({
            element: element,
            role: role,
            name: readProperty(element, "name", ""),
            description: readProperty(element, "description", ""),
            value: value,
            enabled: readProperty(element, "enabled", false)
        });
        var children = readProperty(element, "uiElements", []);
        if (item.depth < 12) {
            for (var i = 0; i < children.length; i++) pending.push({element: children[i], depth: item.depth + 1});
        } else if (children.length) {
            depthTruncated = true;
        }
    }
    return {controls: controls, truncated: pending.length > 0 || depthTruncated};
}

function label(control) {
    return [control.name, control.description, typeof control.value === "string" ? control.value : ""].join(" ");
}

function snapshot(process) {
    var windows = process.windows(), result = [], chooser = null;
    for (var i = 0; i < windows.length; i++) {
        var inspected = inspectWindow(windows[i]);
        var isChooser = inspected.controls.some(function (c) {
            return /select the components.*get started/i.test(label(c));
        });
        if (isChooser) {
            if (chooser !== null) throw new Error("Multiple component chooser windows; refusing to act");
            chooser = inspected;
        }
        result.push({
            name: readProperty(windows[i], "name", ""),
            truncated: inspected.truncated,
            controls: inspected.controls.map(function (c) {
                return {role: c.role, name: c.name, description: c.description, value: c.value, enabled: c.enabled};
            })
        });
    }
    return {chooser: chooser, windows: result};
}

function selectedDownloads(chooser) {
    return chooser.controls.filter(function (c) {
        return c.role === "AXCheckBox" && c.enabled && (c.value === 1 || c.value === true || c.value === "1");
    });
}

function run(args) {
    var pid = Number(args[0]), mode = args[1];
    if (!Number.isInteger(pid) || pid <= 0 || ["probe", "deselect", "continue"].indexOf(mode) < 0)
        throw new Error("Invalid probe arguments");
    var system = Application("System Events");
    var matches = system.processes.whose({unixId: pid})();
    if (matches.length !== 1) throw new Error("Target Xcode PID is no longer present");
    var process = matches[0];
    if (process.name() !== "Xcode") throw new Error("Target PID is not Xcode");
    var state = snapshot(process);
    if (mode === "probe")
        return JSON.stringify({pid: pid, chooser: state.chooser !== null, windows: state.windows});
    if (state.chooser === null || state.chooser.truncated)
        return JSON.stringify({outcome: "unrecognized-window", windows: state.windows});

    var editable = state.chooser.controls.filter(function (c) {
        return c.role === "AXCheckBox" && c.enabled;
    });
    if (!editable.length || editable.some(function (c) {
        return [0, 1, false, true, "0", "1"].indexOf(c.value) < 0;
    })) return JSON.stringify({outcome: "unrecognized-checkbox-state", windows: state.windows});
    var selected = selectedDownloads(state.chooser);
    if (mode === "deselect") {
        if (selected.some(function (c) {
            return !/\b(iOS|watchOS|tvOS|visionOS)\b|Predictive Code Completion Model/i.test(label(c));
        })) return JSON.stringify({outcome: "unrecognized-checkbox", windows: state.windows});
        // Check every selected control before making any changes.
        selected.forEach(function (c) { system.click(c.element); });
        return JSON.stringify({outcome: "deselected", count: selected.length});
    }
    if (selected.length)
        return JSON.stringify({outcome: "downloads-still-selected", windows: state.windows});
    var buttons = state.chooser.controls.filter(function (c) {
        var name = String(c.name || c.description).trim();
        return c.role === "AXButton" && c.enabled && /^(Continue|Download & Install)$/.test(name);
    });
    if (buttons.length !== 1)
        return JSON.stringify({outcome: "unrecognized-continue-button", windows: state.windows});
    system.click(buttons[0].element);
    return JSON.stringify({outcome: "submitted"});
}
