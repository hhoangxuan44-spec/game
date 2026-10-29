-- Spawn-any-unit popup for the Advanced Cheat Menu.
-- Opened from the cheat panel ("Spawn unit" button).  Lists every unit in the game database (paged, filtered by tab),
-- click a unit to create it next to the selected unit / selected city / capital (in that order).
local PAGE_SIZE = 8;
local COUNTS = { 1, 5, 10 };

local m_filter : string = "ALL";
local m_page : number = 1;
local m_countIndex : number = 1;
local m_units : table = {};

-- ---------------------------------------------------------------------------------------------
local function IsMilitary(kUnit)
	return (kUnit.Combat or 0) > 0 or (kUnit.RangedCombat or 0) > 0 or (kUnit.Bombard or 0) > 0;
end

local function Matches(kUnit)
	if m_filter == "ALL" then return true; end
	local military = IsMilitary(kUnit);
	if m_filter == "LAND" then return military and kUnit.Domain == "DOMAIN_LAND"; end
	if m_filter == "SEA" then return military and kUnit.Domain == "DOMAIN_SEA"; end
	if m_filter == "AIR" then return military and kUnit.Domain == "DOMAIN_AIR"; end
	if m_filter == "OTHER" then return not military; end
	return true;
end

local function BuildList()
	m_units = {};
	for kUnit in GameInfo.Units() do
		if kUnit.Name ~= nil and Matches(kUnit) then
			table.insert(m_units, { Type = kUnit.UnitType, Name = Locale.Lookup(kUnit.Name), Cost = kUnit.Cost or 0 });
		end
	end
	-- cheap (early) units first, then by name
	table.sort(m_units, function(a, b)
		if a.Cost ~= b.Cost then return a.Cost < b.Cost; end
		return a.Name < b.Name;
	end);
end

local function GetSpawnPlot()
	local pUnit = UI.GetHeadSelectedUnit();
	if pUnit ~= nil then return pUnit:GetX(), pUnit:GetY(); end
	local pCity = UI.GetHeadSelectedCity();
	if pCity ~= nil then return pCity:GetX(), pCity:GetY(); end
	local pPlayer = Players[Game.GetLocalPlayer()];
	if pPlayer ~= nil then
		local pCapital = pPlayer:GetCities():GetCapitalCity();
		if pCapital ~= nil then return pCapital:GetX(), pCapital:GetY(); end
	end
	return nil, nil;
end

-- ---------------------------------------------------------------------------------------------
local function Refresh()
	local pages = math.max(1, math.ceil(#m_units / PAGE_SIZE));
	if m_page > pages then m_page = pages; end
	if m_page < 1 then m_page = 1; end
	for i = 1, PAGE_SIZE do
		local button = Controls["Unit" .. i];
		local entry = m_units[(m_page - 1) * PAGE_SIZE + i];
		if entry ~= nil then
			button:SetText(entry.Name);
			button:SetHide(false);
		else
			button:SetHide(true);
		end
	end
	Controls.PageLabel:SetText(m_page .. " / " .. pages);
	Controls.CountButton:SetText("x" .. COUNTS[m_countIndex]);
	Controls.PopupStack:CalculateSize();
	Controls.PopupStack:ReprocessAnchoring();
end

local function SetFilter(filter)
	m_filter = filter;
	m_page = 1;
	BuildList();
	Refresh();
end

local function OnUnitClicked(index)
	local entry = m_units[(m_page - 1) * PAGE_SIZE + index];
	if entry == nil then return; end
	local x, y = GetSpawnPlot();
	if x == nil then return; end
	local playerID = Game.GetLocalPlayer();
	if ExposedMembers.MOD_CheatMenu ~= nil and ExposedMembers.MOD_CheatMenu.SpawnUnit ~= nil then
		ExposedMembers.MOD_CheatMenu.SpawnUnit(playerID, entry.Type, x, y, COUNTS[m_countIndex]);
	end
	UI.PlaySound("Play_UI_Click");
end

local function OnClose()
	UIManager:DequeuePopup(ContextPtr);
	ContextPtr:SetHide(true);
end

local function OnInputHandler(uiMsg, wParam, lParam)
	if uiMsg == KeyEvents.KeyUp then
		if wParam == Keys.VK_ESCAPE then
			OnClose();
		end
	end
	return true;
end

local function OnShow()
	if Players[Game.GetLocalPlayer()] == nil then return; end
	BuildList();
	Refresh();
	ContextPtr:SetHide(false);
	ContextPtr:SetInputHandler(OnInputHandler);
	UIManager:QueuePopup(ContextPtr, PopupPriority.Current);
end

-- ---------------------------------------------------------------------------------------------
function Initialize()
	ContextPtr:SetHide(true);
	ContextPtr:SetInputHandler(OnInputHandler, true);
	Controls.PanelHeader:SetText("生成单位");
	Controls.TabAll:SetText("全部");
	Controls.TabLand:SetText("陆军");
	Controls.TabSea:SetText("海军");
	Controls.TabAir:SetText("空军");
	Controls.TabOther:SetText("其他");
	Controls.PrevPage:SetText("<");
	Controls.NextPage:SetText(">");
	Controls.TabAll:RegisterCallback(Mouse.eLClick, function() SetFilter("ALL"); end);
	Controls.TabLand:RegisterCallback(Mouse.eLClick, function() SetFilter("LAND"); end);
	Controls.TabSea:RegisterCallback(Mouse.eLClick, function() SetFilter("SEA"); end);
	Controls.TabAir:RegisterCallback(Mouse.eLClick, function() SetFilter("AIR"); end);
	Controls.TabOther:RegisterCallback(Mouse.eLClick, function() SetFilter("OTHER"); end);
	Controls.PrevPage:RegisterCallback(Mouse.eLClick, function() m_page = m_page - 1; Refresh(); end);
	Controls.NextPage:RegisterCallback(Mouse.eLClick, function() m_page = m_page + 1; Refresh(); end);
	Controls.CountButton:RegisterCallback(Mouse.eLClick, function()
		m_countIndex = m_countIndex % #COUNTS + 1;
		Refresh();
	end);
	for i = 1, PAGE_SIZE do
		Controls["Unit" .. i]:RegisterCallback(Mouse.eLClick, function() OnUnitClicked(i); end);
	end
	Controls.ModalScreenClose:RegisterCallback(Mouse.eLClick, OnClose);
	LuaEvents.CheatMenu_ShowSpawnUnit.Add(OnShow);
end
Initialize();
