--[[
    charcapture - snapshot a character for porting to Vanadreams.

    /capture         write config\charcapture\<Name>.json and print a summary
    /capture show    print the last summary again

    Reads only through Ashita's memory managers. Sends nothing, changes nothing.
    Runs on retail or any server; the file records which.
]]

addon.name    = 'charcapture';
addon.author  = 'Vanadreams';
addon.version = '0.1.2';
addon.desc    = 'Captures a character snapshot for porting to Vanadreams.';
addon.link    = 'https://github.com/VanaDreams/vanadreams-ashita';

require('common');
local json = require('json');

local last_summary = nil;

local function hex(s)
    if s == nil then return ''; end
    return (s:gsub('.', function (c) return ('%02x'):format(c:byte()); end));
end

-- Spent merits and job point upgrades are not in Ashita's memory managers; the game only sends
-- them when the Merit Points and Job Points menus are opened. Those packets are remembered here
-- and go into the next snapshot.
--   0x08C: u16 count at 0x04, then count entries of { u16 merit id, u8 next cost, u8 upgrades }
--   0x08D: 64 entries of { u16 index:5 | job:11, u16 next:10 | level:6 } from 0x04
local seen = T{ merits = T{}, merits_at = nil, job_points = T{}, job_points_at = nil, logs = T{}, logs_at = nil };

-- Quest and mission logs arrive as 0x056 packets when you zone: a 32-byte block at 0x04 and a
-- 'port' at 0x24 saying which log it is (current or completed quests per area, completed
-- missions, and 0xFFFF for the current missions). The blocks are kept raw, keyed by port; the
-- importer knows the layout because the server builds these same packets.
local function read_log(data)
    if #data < 0x28 then return; end
    local port = struct.unpack('<H', data, 0x25);
    if port == nil then return; end
    seen.logs[('%04x'):format(port)] = hex(data:sub(5, 36));
    seen.logs_at = os.time();
end

local function read_merits(data)
    local count = struct.unpack('<H', data, 0x05);
    if count == nil or count > 61 then return; end
    for i = 0, count - 1 do
        local id, _, upgrades = struct.unpack('<HBB', data, 0x09 + i * 4);
        if id and id > 0 and upgrades > 0 then seen.merits[tostring(id)] = upgrades; end
    end
    seen.merits_at = os.time();
end

local function read_job_points(data)
    for i = 0, 63 do
        local a, b = struct.unpack('<HH', data, 0x05 + i * 4);
        if a == nil or a == 0 then break; end
        local index, job = bit.band(a, 0x1F), bit.rshift(a, 5);
        local level = bit.rshift(b, 10);
        if job > 0 and level > 0 then
            seen.job_points[tostring(job)] = seen.job_points[tostring(job)] or T{};
            seen.job_points[tostring(job)][tostring(index)] = level;
        end
    end
    seen.job_points_at = os.time();
end

local JOB_COUNT = 22;        -- 1 WAR .. 22 RUN
local COMBAT_SKILLS = 48;    -- Ashita combat skill indexes
local CRAFT_SKILLS = 10;     -- fishing, woodworking, smithing, goldsmithing, clothcraft, leathercraft, bonecraft, alchemy, cooking, synergy
local SPELL_MAX = 1024;
local ABILITY_MAX = 1024;
local WEAPONSKILL_MAX = 256;
local TRAIT_MAX = 256;
local KEYITEM_MAX = 3072;
local CONTAINERS = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
local EQUIP_SLOTS = 16;

local function say(msg)
    print(('\30\08[charcapture]\30\01 %s'):format(msg));
end

local function server_name()
    local cmd = '';
    pcall(function() cmd = AshitaCore:GetConfigurationManager():GetString('boot', 'ashita.boot', 'command') or ''; end);
    local file = '';
    pcall(function() file = AshitaCore:GetConfigurationManager():GetString('boot', 'ashita.boot', 'file') or ''; end);
    local server = cmd:match('%-%-server%s+(%S+)');
    if server then return server; end
    if file:lower():find('xiloader', 1, true) then return 'private'; end
    return 'retail';
end

local function safe(f, default)
    local ok, v = pcall(f);
    if ok and v ~= nil then return v; end
    return default;
end

local function capture()
    local mm = AshitaCore:GetMemoryManager();
    local player = mm:GetPlayer();
    local inv = mm:GetInventory();
    local party = mm:GetParty();
    local ent = mm:GetEntity();

    if player:GetLoginStatus() ~= 2 then say('log in first'); return nil; end
    local idx = party:GetMemberTargetIndex(0);
    local name = party:GetMemberName(0);
    if name == nil or #name == 0 then name = safe(function() return ent:GetName(idx); end, 'Unknown'); end

    local snap = T{
        schema = 1, addon = 'charcapture', addon_version = addon.version,
        captured_at = os.date('!%Y-%m-%dT%H:%M:%SZ'),
        captured_on = server_name(),
        character = T{
            name = name,
            server_id = party:GetMemberServerId(0),
            race = safe(function() return ent:GetRace(idx); end, 0),
            face = safe(function() return ent:GetLookHair(idx); end, 0),
            size = safe(function() return ent:GetModelSize(idx); end, 0),
            nation = safe(function() return player:GetNation(); end, 0),
            rank = safe(function() return player:GetRank(); end, 0),
            rank_points = safe(function() return player:GetRankPoints(); end, 0),
            title = safe(function() return player:GetTitle(); end, 0),
            homepoint = safe(function() return player:GetHomepoint(); end, 0),
            residence = safe(function() return player:GetResidence(); end, 0),
        },
        jobs = T{
            main = player:GetMainJob(), main_level = player:GetMainJobLevel(),
            sub = player:GetSubJob(), sub_level = player:GetSubJobLevel(),
            levels = T{}, master_levels = T{}, job_points = T{},
            merits = T{ points = safe(function() return player:GetMeritPoints(); end, 0), max = safe(function() return player:GetMeritPointsMax(); end, 0) },
            limit_points = safe(function() return player:GetLimitPoints(); end, 0),
            exp = T{ current = safe(function() return player:GetExpCurrent(); end, 0), needed = safe(function() return player:GetExpNeeded(); end, 0) },
        },
        skills = T{ combat = T{}, craft = T{} },
        spells = T{}, abilities = T{}, weaponskills = T{}, traits = T{}, key_items = T{},
        gil = 0,
        inventory = T{},
        equipment = T{},
        look = T{
            hair = safe(function() return ent:GetLookHair(idx); end, 0),
            head = safe(function() return ent:GetLookHead(idx); end, 0),
            body = safe(function() return ent:GetLookBody(idx); end, 0),
            hands = safe(function() return ent:GetLookHands(idx); end, 0),
            legs = safe(function() return ent:GetLookLegs(idx); end, 0),
            feet = safe(function() return ent:GetLookFeet(idx); end, 0),
            main = safe(function() return ent:GetLookMain(idx); end, 0),
            sub = safe(function() return ent:GetLookSub(idx); end, 0),
            ranged = safe(function() return ent:GetLookRanged(idx); end, 0),
        },
        -- from the menus, when they were opened this session (see the packet notes at the top)
        merit_upgrades = seen.merits_at and seen.merits or nil,
        job_point_upgrades = seen.job_points_at and seen.job_points or nil,
        quest_mission_packets = seen.logs_at and seen.logs or nil,
        not_captured = T{ 'fame (the game never sends the number)', 'linkshells', 'mog house layout' },
    };
    if not seen.merits_at then snap.not_captured:append('merit upgrades (open the Merit Points menu, then /capture again)'); end
    if not seen.job_points_at then snap.not_captured:append('job point upgrades (open the Job Points menu, then /capture again)'); end
    if not seen.logs_at then snap.not_captured:append('quests and missions (zone once with charcapture loaded, then /capture again)'); end

    -- jobs
    for job = 1, JOB_COUNT do
        snap.jobs.levels[tostring(job)] = safe(function() return player:GetJobLevel(job); end, 0);
        snap.jobs.master_levels[tostring(job)] = safe(function() return player:GetJobMasterLevel(job); end, 0);
        snap.jobs.job_points[tostring(job)] = T{
            points = safe(function() return player:GetJobPoints(job); end, 0),
            spent = safe(function() return player:GetJobPointsSpent(job); end, 0),
            capacity = safe(function() return player:GetCapacityPoints(job); end, 0),
        };
    end

    -- skills
    for i = 0, COMBAT_SKILLS - 1 do
        local s = safe(function() return player:GetCombatSkill(i); end, nil);
        if s then snap.skills.combat[tostring(i)] = T{ skill = safe(function() return s:GetSkill(); end, 0), rank = safe(function() return s:GetRank(); end, 0), capped = safe(function() return s:IsCapped(); end, false) }; end
    end
    for i = 0, CRAFT_SKILLS - 1 do
        local s = safe(function() return player:GetCraftSkill(i); end, nil);
        if s then snap.skills.craft[tostring(i)] = T{ skill = safe(function() return s:GetSkill(); end, 0), rank = safe(function() return s:GetRank(); end, 0), capped = safe(function() return s:IsCapped(); end, false) }; end
    end

    -- known things: every id the client says yes to
    for id = 0, SPELL_MAX - 1 do if safe(function() return player:HasSpell(id); end, false) then snap.spells:append(id); end end
    for id = 0, ABILITY_MAX - 1 do if safe(function() return player:HasAbility(id); end, false) then snap.abilities:append(id); end end
    for id = 0, WEAPONSKILL_MAX - 1 do if safe(function() return player:HasWeaponSkill(id); end, false) then snap.weaponskills:append(id); end end
    for id = 0, TRAIT_MAX - 1 do if safe(function() return player:HasTrait(id); end, false) then snap.traits:append(id); end end
    for id = 0, KEYITEM_MAX - 1 do if safe(function() return player:HasKeyItem(id); end, false) then snap.key_items:append(id); end end

    -- bags
    local item_count = 0;
    for _, c in ipairs(CONTAINERS) do
        local max = safe(function() return inv:GetContainerCountMax(c); end, 0);
        local list = T{};
        if max and max > 0 then
            -- slot 0 of the inventory is gil
            local g = safe(function() return inv:GetContainerItem(0, 0); end, nil);
            if c == 0 and g and g.Id == 65535 then snap.gil = g.Count; end
            for slot = 1, max do
                local it = safe(function() return inv:GetContainerItem(c, slot); end, nil);
                if it and it.Id ~= 0 and it.Id ~= 65535 and it.Count > 0 then
                    list:append(T{ slot = slot, id = it.Id, count = it.Count, flags = it.Flags, price = it.Price, extra = hex(it.Extra) });
                    item_count = item_count + 1;
                end
            end
        end
        snap.inventory[tostring(c)] = list;
    end

    -- equipment
    for slot = 0, EQUIP_SLOTS - 1 do
        local e = safe(function() return inv:GetEquippedItem(slot); end, nil);
        if e and e.Index ~= 0 then
            local container = math.floor(bit.band(e.Index, 0xFF00) / 0x0100);
            local index = e.Index % 0x0100;
            local it = safe(function() return inv:GetContainerItem(container, index); end, nil);
            snap.equipment[tostring(slot)] = T{ container = container, slot = index, id = it and it.Id or 0 };
        end
    end

    -- write
    local dir = ('%sconfig\\charcapture\\'):format(AshitaCore:GetInstallPath());
    ashita.fs.create_directory(dir);
    local path = dir .. name:gsub('[^%w]', '') .. '.json';
    local f = io.open(path, 'w');
    if not f then say('could not write ' .. path); return nil; end
    f:write(json.encode(snap));
    f:close();

    local res = AshitaCore:GetResourceManager();
    local job = res:GetString('jobs.names_abbr', snap.jobs.main) or tostring(snap.jobs.main);
    last_summary = ('%s, %s%d, %d items across all bags, %d gil, %d spells, %d key items -> %s'):format(name, job, snap.jobs.main_level, item_count, snap.gil, #snap.spells, #snap.key_items, path);
    say(last_summary);
    say('now send it from the launcher: Capture > Send to Vanadreams.');
    return path;
end

ashita.events.register('command', 'charcapture_cmd', function (e)
    local args = e.command:args();
    if #args == 0 or (args[1] ~= '/capture' and args[1] ~= '/charcapture') then return; end
    e.blocked = true;
    local sub = (args[2] or ''):lower();
    if sub == 'show' then
        say(last_summary or 'nothing captured yet; /capture to take a snapshot');
    else
        capture();
    end
end);

ashita.events.register('packet_in', 'charcapture_packet_in', function (e)
    if e.id == 0x08C then pcall(read_merits, e.data);
    elseif e.id == 0x08D then pcall(read_job_points, e.data);
    elseif e.id == 0x056 then pcall(read_log, e.data); end
end);

ashita.events.register('load', 'charcapture_load', function ()
    say('loaded. Zone once, open the Merit Points and Job Points menus, then /capture writes your character snapshot.');
end);
