using System.Collections.Concurrent;
using System.Collections.Generic;

using UnityEngine;

namespace JumpSlug;

static class InputHelper {
    private static readonly Dictionary<KeyCode, bool> PressedMap = new Dictionary<KeyCode, bool>();
    private static readonly bool[] PressedMouseButtons = new bool[3];
    public static bool JustPressed(KeyCode key) {
        if (!PressedMap.TryGetValue(key, out bool justPressed)) {
            PressedMap.Add(key, false);
        }
        switch (Input.GetKey(key), justPressed) {
            case (true, false):
                PressedMap[key] = true;
                return true;
            case (false, true):
                PressedMap[key] = false;
                break;
        }
        return false;
    }
    public static bool JustPressedMouseButton(int mouseButton) {
        ref bool justPressed = ref PressedMouseButtons[mouseButton];
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