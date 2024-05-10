using System.Collections.Concurrent;
using System.Collections.Generic;

using UnityEngine;

namespace JumpSlug;

static class InputHelper {
    private static Dictionary<KeyCode, bool> pressedMap = new Dictionary<KeyCode, bool>();
    private static bool[] pressedMouseButtons = new bool[3];
    public static bool JustPressed(KeyCode key) {
        if (!pressedMap.TryGetValue(key, out bool justPressed)) {
            pressedMap.Add(key, false);
        }
        switch (Input.GetKey(key), justPressed) {
            case (true, false):
                pressedMap[key] = true;
                return true;
            case (false, true):
                pressedMap[key] = false;
                break;
        }
        return false;
    }
    public static bool JustPressedMouseButton(int mouseButton) {
        ref bool justPressed = ref pressedMouseButtons[mouseButton];
        switch (Input.GetMouseButton(mouseButton), justPressed) {
            case (true, false):
                justPressed = true;
                return true;
            case (false, true):
                justPressed = false;
                break;
        }
        return false;
    }
}