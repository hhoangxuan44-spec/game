#!/usr/bin/env python3
"""Adds the "spawn any unit" feature to the Advanced Cheat Menu mod folder (idempotent).

usage: patch_mod.py <mod folder containing CheatMenuPanel.modinfo>
"""
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, "mod-patch")


def edit(path, old, new, marker):
    s = open(path, encoding="utf-8-sig").read()
    if marker in s:
        return
    if old not in s:
        sys.exit("patch anchor not found in %s: %r" % (path, old[:60]))
    open(path, "w", encoding="utf-8").write(s.replace(old, new, 1))


def main(mod):
    for name in ("Cheat_Spawn_Unit_Popup.xml", "Cheat_Spawn_Unit_Popup.lua"):
        shutil.copy(os.path.join(PATCH, name), os.path.join(mod, "UI", "Popups", name))

    # 1. modinfo: register the popup
    info = os.path.join(mod, "CheatMenuPanel.modinfo")
    edit(info, "<File>UI/Popups/Raze_City_Reminder_Popup.xml</File>\n\t\t\t\t<File>UI/Panels/Cheat_Panel_World_Tracker.xml</File>",
         "<File>UI/Popups/Raze_City_Reminder_Popup.xml</File>\n\t\t\t\t<File>UI/Popups/Cheat_Spawn_Unit_Popup.xml</File>\n\t\t\t\t<File>UI/Panels/Cheat_Panel_World_Tracker.xml</File>",
         "Cheat_Spawn_Unit_Popup.xml</File>\n\t\t\t\t<File>UI/Panels")
    edit(info, "\t\t<File>UI/Popups/Raze_City_Reminder_Popup.xml</File>\n\t\t<File>UI/Panels/Cheat_Panel_World_Tracker.xml</File>",
         "\t\t<File>UI/Popups/Raze_City_Reminder_Popup.xml</File>\n\t\t<File>UI/Popups/Cheat_Spawn_Unit_Popup.xml</File>\n\t\t<File>UI/Popups/Cheat_Spawn_Unit_Popup.lua</File>\n\t\t<File>UI/Panels/Cheat_Panel_World_Tracker.xml</File>",
         "Cheat_Spawn_Unit_Popup.lua")

    # 2. panel: one more row with a "spawn unit" button, taller panel
    xml = os.path.join(mod, "UI", "Panels", "Cheat_Menu_Panel.xml")
    edit(xml, 'Size="445,185" ID="SpawnDlgContainer"', 'Size="445,235" ID="SpawnDlgContainer"', 'Size="445,235"')
    edit(xml, 'Begin="0,185"', 'Begin="0,235"', 'Begin="0,235"')
    edit(xml, 'ID="Top" Size="445,185"', 'ID="Top" Size="445,235"', 'ID="Top" Size="445,235"')
    row = ('\t\t\t\t<Grid ID="BonusBacking4" Anchor="C,T" Offset="0,0" Size="405,46" Texture="Controls_SubContainer2" SliceCorner="17,17" SliceSize="1,1" SliceTextureSize="36,35" Color="GovBonusDark">\n'
           '\t\t\t\t\t<GridButton ID="CheatButtonSpawn" Anchor="C,C" Size="200,34" Style="TabButton" FontSize="14" TextOffset="0,2"/>\n'
           '\t\t\t\t</Grid>\n')
    edit(xml, "\t\t\t</Stack>\n\t\t</SlideAnim>", row + "\t\t\t</Stack>\n\t\t</SlideAnim>", "BonusBacking4")

    lua = os.path.join(mod, "UI", "Panels", "Cheat_Menu_Panel_Functions.lua")
    # nothing to add there; the button is wired in Cheat_Menu_Panel.lua
    panel_lua = os.path.join(mod, "UI", "Panels", "Cheat_Menu_Panel.lua")
    edit(panel_lua, "\t\tControls.SpawnDlgContainer:SetHide( true );\nend",
         "\t\tControls.CheatButtonSpawn:SetText(\"生成单位\");\n"
         "\t\tControls.CheatButtonSpawn:RegisterCallback(Mouse.eLClick, function() LuaEvents.CheatMenu_ShowSpawnUnit(); end);\n"
         "\t\tControls.SpawnDlgContainer:SetHide( true );\nend", "CheatMenu_ShowSpawnUnit")

    # 3. gameplay side: the function that actually creates the units
    script = os.path.join(mod, "UI", "Script", "Cheat_Menu_Panel_Script.lua")
    edit(script, "function UnitHealChange(playerID, unitID)",
         "function SpawnUnit(playerID, unitType, x, y, count)\n"
         "\tlocal pPlayer = Players[playerID];\n"
         "\tif pPlayer == nil or not pPlayer:IsHuman() then return; end\n"
         "\tfor i = 1, (count or 1) do\n"
         "\t\tUnitManager.InitUnitValidAdjacentHex(playerID, unitType, x, y, 1);\n"
         "\tend\n"
         "end\n"
         "function UnitHealChange(playerID, unitID)", "function SpawnUnit")
    edit(script, "\tExposedMembers.MOD_CheatMenu.UnitHealChange = UnitHealChange;",
         "\tExposedMembers.MOD_CheatMenu.UnitHealChange = UnitHealChange;\n\tExposedMembers.MOD_CheatMenu.SpawnUnit = SpawnUnit;",
         "MOD_CheatMenu.SpawnUnit")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
