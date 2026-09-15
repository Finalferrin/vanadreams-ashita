--[[
    vanafish - a fishing bot for Vanadreams.

    Runs on any server. Casts, waits for the bite, reads the fight numbers the
    server sends, decides catch or release by your rules, ends the fight at the packet
    level the way the server judges it, and stops itself when something is off.

    Commands:
      /vanafish            toggle the window
      /vanafish start      start fishing
      /vanafish stop       stop fishing
      /vanafish status     print the counters
      /vanafish reset      reset the counters

    Server-side: the server judges the end of a fight on two numbers only, the remaining
    stamina and the echoed 'special' value from the bite packet. Stamina 0-4 with the right
    special runs the catch roll. Everything about the gauge is client-side. There is no
    fatigue, no daily limit and no cadence check on Vanadreams; the only pacing is the
    five-second cast lock and the per-area fish stock.
]]

addon.name    = 'vanafish';
addon.author  = 'Vanadreams';
addon.version = '0.1.1';
addon.desc    = 'Fishing bot for the Vanadreams server.';
addon.link    = 'https://github.com/Finalferrin/vanadreams-ashita';

require('common');
local imgui    = require('imgui');
local settings = require('settings');

-- ---------------------------------------------------------------------------
-- settings
-- ---------------------------------------------------------------------------
local defaults = T{
    window_open        = T{ true },
    -- catch policy
    catch_small_fish   = T{ true },
    catch_large_fish   = T{ true },
    catch_items        = T{ true },
    catch_monsters     = T{ false },
    release_on_noskill = T{ true },   -- 'don't have the skill' feelings mean a near-certain loss
    release_chance     = T{ 0 },      -- percent of good bites released anyway, to look human
    -- timing
    reel_delay_min     = T{ 4 },      -- seconds after the bite before ending the fight
    reel_delay_max     = T{ 12 },
    cast_delay_min     = T{ 6 },      -- seconds between casts (server lock is 5)
    cast_delay_max     = T{ 10 },
    -- stops
    stop_free_slots    = T{ 1 },      -- stop when free inventory slots fall to this
    stop_on_tell       = T{ true },
    stop_on_move       = T{ true },
    stop_max_casts     = T{ 0 },      -- 0 = no limit
    stop_max_minutes   = T{ 0 },
    stop_at_skill      = T{ 0 },      -- 0 = no target (skill in whole points, 0-100+)
    stop_nothing_streak = T{ 15 },    -- consecutive 'didn't catch anything' before stopping
    -- extra
    alert_command      = T{ '' },     -- e.g. "/echo done" - run when the bot stops itself
    log_catches        = T{ true },
    servers            = T{ 'vanadreams' }, -- only used to name the server in the window; the bot runs anywhere
};

local cfg = settings.load(defaults);

-- ---------------------------------------------------------------------------
-- state
-- ---------------------------------------------------------------------------
local S = {
    IDLE = 'idle', CASTING = 'casting', WAITING = 'waiting for a bite', HOOKED = 'hooked',
    ENDING = 'ending the fight', RESOLVING = 'resolving', COOLDOWN = 'cooldown', STOPPED = 'stopped',
};

local bot = {
    running        = false,
    state          = S.STOPPED,
    stop_reason    = '',
    since          = 0,          -- os.clock() when the state was entered
    deadline       = 0,          -- os.clock() when the state times out
    started_at     = 0,
    reel_at        = 0,
    injected_end   = false,      -- we sent mode 3; block the client's own
    fish           = nil,        -- last 0x115
    bite           = nil,        -- 'small', 'large', 'item', 'monster'
    feeling        = nil,        -- 'good', 'bad', 'terrible', 'noskill', 'noskill_unsure', 'noskill_positive', 'keen'
    decision       = nil,        -- 'catch' / 'release'
    nothing_streak = 0,
    start_pos      = nil,
    last_result    = '',
    counters       = T{ casts = 0, bites = 0, catches = 0, releases = 0, lost = 0, breaks = 0, nothing = 0, skillups = 0 },
    log            = T{},
    allowed        = false,
    allowed_reason = '',
};

local function now() return os.clock(); end
local function rnd(lo, hi) if hi <= lo then return lo; end return lo + math.random() * (hi - lo); end

local function say(msg)
    print(('\30\08[vanafish]\30\01 %s'):format(msg));
end

local function enter(state, timeout)
    bot.state = state;
    bot.since = now();
    bot.deadline = timeout and (bot.since + timeout) or 0;
end

local function stop(reason)
    if not bot.running then return; end
    bot.running = false;
    bot.stop_reason = reason or '';
    enter(S.STOPPED);
    say('stopped: ' .. bot.stop_reason);
    local cmd = cfg.alert_command[1];
    if cmd and #cmd > 0 then AshitaCore:GetChatManager():QueueCommand(1, cmd); end
end

-- ---------------------------------------------------------------------------
-- game access
-- ---------------------------------------------------------------------------
local SLOT_RANGE, SLOT_AMMO = 2, 3;

local function equipped_item(slot)
    local inv = AshitaCore:GetMemoryManager():GetInventory();
    local e = inv:GetEquippedItem(slot);
    if e == nil or e.Index == 0 then return nil; end
    local item = inv:GetContainerItem(bit.band(e.Index, 0xFF00) / 0x0100, e.Index % 0x0100);
    if item == nil or item.Id == 0 or item.Id == 65535 then return nil; end
    return item;
end

local function item_name(id)
    local r = AshitaCore:GetResourceManager():GetItemById(id);
    return r and r.Name[1] or ('item ' .. tostring(id));
end

local function free_slots()
    local inv = AshitaCore:GetMemoryManager():GetInventory();
    return inv:GetContainerCountMax(0) - inv:GetContainerCount(0);
end

local function fishing_skill()
    local p = AshitaCore:GetMemoryManager():GetPlayer();
    local ok, skill = pcall(function() return p:GetCraftSkill(0):GetSkill(); end);
    return ok and skill or 0;
end

local function my_ids()
    local party = AshitaCore:GetMemoryManager():GetParty();
    return party:GetMemberServerId(0), party:GetMemberTargetIndex(0);
end

local function my_pos()
    local sid, idx = my_ids();
    local ent = AshitaCore:GetMemoryManager():GetEntity();
    return ent:GetLocalPositionX(idx), ent:GetLocalPositionY(idx), ent:GetLocalPositionZ(idx);
end

local function moved_far(from)
    if from == nil then return false; end
    local x, y, z = my_pos();
    local dx, dy, dz = x - from[1], y - from[2], z - from[3];
    return (dx * dx + dy * dy + dz * dz) > 4.0;
end

local function logged_in()
    return AshitaCore:GetMemoryManager():GetPlayer():GetLoginStatus() == 2;
end

-- The bot runs on any server. It still reads the boot command so the window can say where it is;
-- the lock to Vanadreams came out in 0.1.1 at Lee's ruling.
local function check_server()
    local cm = AshitaCore:GetConfigurationManager();
    local cmd = '';
    pcall(function() cmd = cm:GetString('boot', 'ashita.boot', 'command') or ''; end);
    cmd = cmd:lower();
    bot.allowed = true;
    bot.allowed_reason = 'any server';
    for _, name in ipairs(cfg.servers) do
        if #name > 0 and cmd:find(name:lower(), 1, true) then bot.allowed_reason = 'server: ' .. name; break; end
    end
    return true;
end

-- ---------------------------------------------------------------------------
-- packets
-- ---------------------------------------------------------------------------
-- 0x110: id 0x110 -> low byte 0x10, header byte 2 = (size_in_words << 1) | (id >> 8) = (10 << 1) | 1 = 0x15
local function send_fishing(mode, para, para2)
    local sid, idx = my_ids();
    local p = struct.pack('<BBHIiHbBi', 0x10, 0x15, 0, sid, para, idx, mode, 0, para2):totable();
    AshitaCore:GetPacketManager():AddOutgoingPacket(0x110, p);
end

local function end_fight(catch)
    if bot.fish == nil then return; end
    bot.injected_end = true;
    if catch then
        send_fishing(3, 0, bot.fish.special);       -- exhausted fish, special echoed: the server rolls the catch
    else
        send_fishing(3, 200, 0);                     -- give up: no roll, bait kept unless a fish was hooked
    end
    enter(S.RESOLVING, 8);
end

local function cast()
    bot.start_pos = bot.start_pos or T{ my_pos() };
    bot.fish, bot.bite, bot.feeling, bot.decision = nil, nil, nil, nil;
    bot.injected_end = false;
    AshitaCore:GetChatManager():QueueCommand(1, '/fish');
    bot.counters.casts = bot.counters.casts + 1;
    enter(S.CASTING, 6);
end

-- ---------------------------------------------------------------------------
-- decisions
-- ---------------------------------------------------------------------------
local function decide()
    local c = cfg;
    local want = true;
    if bot.bite == 'monster' then want = c.catch_monsters[1];
    elseif bot.bite == 'item' then want = c.catch_items[1];
    elseif bot.bite == 'large' then want = c.catch_large_fish[1];
    else want = c.catch_small_fish[1]; end
    -- 'noskill' and 'noskill_unsure' are warnings; 'noskill_positive' is the opposite and is kept
    if want and c.release_on_noskill[1] and (bot.feeling == 'noskill' or bot.feeling == 'noskill_unsure') then want = false; end
    if want and c.release_chance[1] > 0 and math.random(100) <= c.release_chance[1] then want = false; end
    return want and 'catch' or 'release';
end

local function record(kind, detail)
    bot.last_result = detail or kind;
    if not cfg.log_catches[1] then return; end
    local line = ('%s,%s,%s,%s,%s,%s'):format(os.date('%Y-%m-%d %H:%M:%S'), kind, detail or '', bot.bite or '', bot.feeling or '', tostring(fishing_skill()));
    bot.log:append(line);
    local dir = ('%sconfig\\vanafish\\'):format(AshitaCore:GetInstallPath());
    ashita.fs.create_directory(dir);
    local f = io.open(dir .. 'catchlog.csv', 'a');
    if f then f:write(line, '\n'); f:close(); end
end

-- ---------------------------------------------------------------------------
-- safety checks, run every tick while running
-- ---------------------------------------------------------------------------
local function safety()
    if not logged_in() then stop('not logged in'); return false; end
    if AshitaCore:GetMemoryManager():GetPlayer():GetIsZoning() ~= 0 then stop('zoning'); return false; end
    if free_slots() <= cfg.stop_free_slots[1] then stop('inventory is full'); return false; end
    if equipped_item(SLOT_RANGE) == nil then stop('no rod equipped'); return false; end
    if equipped_item(SLOT_AMMO) == nil then stop('out of bait'); return false; end
    if cfg.stop_on_move[1] and moved_far(bot.start_pos) then stop('you moved'); return false; end
    if cfg.stop_max_casts[1] > 0 and bot.counters.casts >= cfg.stop_max_casts[1] then stop('cast limit reached'); return false; end
    if cfg.stop_max_minutes[1] > 0 and (now() - bot.started_at) / 60 >= cfg.stop_max_minutes[1] then stop('time limit reached'); return false; end
    if cfg.stop_at_skill[1] > 0 and fishing_skill() >= cfg.stop_at_skill[1] * 10 then stop('skill target reached'); return false; end
    if cfg.stop_nothing_streak[1] > 0 and bot.nothing_streak >= cfg.stop_nothing_streak[1] then stop('nothing biting here any more'); return false; end
    return true;
end

-- ---------------------------------------------------------------------------
-- tick
-- ---------------------------------------------------------------------------
local function tick()
    if not bot.running then return; end
    if not safety() then return; end
    local t = now();
    if bot.state == S.IDLE then
        cast();
    elseif bot.state == S.CASTING then
        -- the client's own 0x01A confirms the cast (packet_out); a timeout means the cast was refused
        if bot.deadline > 0 and t > bot.deadline then bot.counters.nothing = bot.counters.nothing + 1; enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1])); end
    elseif bot.state == S.WAITING then
        if bot.deadline > 0 and t > bot.deadline then enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1])); end
    elseif bot.state == S.HOOKED then
        if t >= bot.reel_at then
            bot.decision = decide();
            if bot.decision == 'catch' then end_fight(true); else bot.counters.releases = bot.counters.releases + 1; end_fight(false); end
        end
    elseif bot.state == S.RESOLVING then
        if bot.deadline > 0 and t > bot.deadline then enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1])); end
    elseif bot.state == S.COOLDOWN then
        if t > bot.deadline then enter(S.IDLE); end
    end
end

-- ---------------------------------------------------------------------------
-- events
-- ---------------------------------------------------------------------------
ashita.events.register('packet_out', 'vanafish_out', function (e)
    if e.id == 0x01A and bot.running and bot.state == S.CASTING then
        local action = struct.unpack('<H', e.data, 0x0A + 1);
        if action == 0x0E then enter(S.WAITING, 30); end
    elseif e.id == 0x110 and not e.injected and bot.running then
        local mode = struct.unpack('<b', e.data, 0x0E + 1);
        if mode == 3 and bot.injected_end then
            e.blocked = true;   -- we already ended the fight; a second end would hit a dead token
        end
    end
end);

ashita.events.register('packet_in', 'vanafish_in', function (e)
    if e.id == 0x115 then
        local stamina, arrow_delay, regen, move_freq, arrow_dmg, arrow_regen, time, sense, _, special = struct.unpack('<HHHHHHHBBI', e.data, 0x04 + 1);
        bot.fish = T{ stamina = stamina, arrow_delay = arrow_delay, regen = regen - 128, move_freq = move_freq, arrow_dmg = arrow_dmg, arrow_regen = arrow_regen, time = time, sense = sense, special = special };
        if bot.running then
            bot.counters.bites = bot.counters.bites + 1;
            bot.nothing_streak = 0;
            local lo, hi = cfg.reel_delay_min[1], cfg.reel_delay_max[1];
            hi = math.min(hi, math.max(2, time - 3));   -- never run the client's own timer out
            lo = math.min(lo, hi);
            bot.reel_at = now() + rnd(lo, hi);
            enter(S.HOOKED, time + 2);
        end
    elseif e.id == 0x00B then
        if bot.running then stop('zoning'); end
    end
end);

ashita.events.register('text_in', 'vanafish_text', function (e)
    if not bot.running then return; end
    local m = e.message:lower();
    -- what bit
    if m:find('something caught the hook!!!', 1, true) then bot.bite = 'large';
    elseif m:find('something caught the hook', 1, true) then bot.bite = 'small';
    elseif m:find('something clamps onto your line', 1, true) then bot.bite = 'monster';
    elseif m:find('you feel something pulling at your line', 1, true) then bot.bite = 'item';
    -- feelings
    elseif m:find('good feeling', 1, true) then bot.feeling = 'good';
    elseif m:find('terrible feeling', 1, true) then bot.feeling = 'terrible';
    elseif m:find('bad feeling', 1, true) then bot.feeling = 'bad';
    elseif m:find('not sure if you have the skill', 1, true) then bot.feeling = 'noskill_unsure';
    elseif m:find("don't have the skill", 1, true) or m:find('lack the skill', 1, true) then bot.feeling = 'noskill';
    elseif m:find('positive you have the skill', 1, true) then bot.feeling = 'noskill_positive';
    elseif m:find("angler's sense", 1, true) then bot.feeling = 'keen';
    -- results
    elseif m:find('you caught', 1, true) then
        bot.counters.catches = bot.counters.catches + 1;
        record('catch', e.message:gsub('^.-you caught an? ', ''):gsub('[!%.]+$', ''));
        enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1]));
    elseif m:find("didn't catch anything", 1, true) or m:find('you didn\'t catch anything', 1, true) then
        bot.counters.nothing = bot.counters.nothing + 1;
        bot.nothing_streak = bot.nothing_streak + 1;
        record('nothing');
        enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1]));
    elseif m:find('your line breaks', 1, true) or m:find('your rod breaks', 1, true) then
        bot.counters.breaks = bot.counters.breaks + 1;
        record('break', e.message);
        if m:find('rod breaks', 1, true) then stop('rod broke'); return; end
        enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1]));
    elseif m:find('you lost your catch', 1, true) or m:find('lack of skill', 1, true) or m:find('you give up', 1, true) or m:find('too small', 1, true) or m:find('too big', 1, true) then
        if bot.decision == 'catch' then bot.counters.lost = bot.counters.lost + 1; record('lost', e.message); else record('released'); end
        enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1]));
    elseif m:find('inventory is full', 1, true) then
        stop('inventory is full'); return;
    elseif m:find("can't fish", 1, true) or m:find('cannot fish', 1, true) or m:find('fishing is currently disabled', 1, true) or m:find('too low to fish', 1, true) then
        bot.counters.nothing = bot.counters.nothing + 1;
        bot.nothing_streak = bot.nothing_streak + 1;
        if m:find('disabled', 1, true) or m:find('too low', 1, true) then stop(e.message); return; end
        enter(S.COOLDOWN, rnd(cfg.cast_delay_min[1], cfg.cast_delay_max[1]));
    elseif m:find('fishing skill rises', 1, true) or (m:find('skill rises', 1, true) and m:find('fishing', 1, true)) then
        bot.counters.skillups = bot.counters.skillups + 1;
        record('skillup', e.message);
    end
    -- a tell while running
    if cfg.stop_on_tell[1] and e.mode == 4 then stop('tell received'); end
end);

-- ---------------------------------------------------------------------------
-- commands
-- ---------------------------------------------------------------------------
local function start()
    if bot.running then return; end
    if not check_server() then say(bot.allowed_reason); return; end
    if not logged_in() then say('log in first'); return; end
    math.randomseed(os.time());
    bot.running = true;
    bot.started_at = now();
    bot.nothing_streak = 0;
    bot.start_pos = nil;
    enter(S.IDLE);
    say('started (' .. bot.allowed_reason .. ')');
end

ashita.events.register('command', 'vanafish_cmd', function (e)
    local args = e.command:args();
    if #args == 0 or args[1] ~= '/vanafish' then return; end
    e.blocked = true;
    local sub = (args[2] or ''):lower();
    if sub == 'start' then start();
    elseif sub == 'stop' then stop('by command');
    elseif sub == 'reset' then for k in pairs(bot.counters) do bot.counters[k] = 0; end bot.log = T{}; say('counters reset');
    elseif sub == 'status' then
        local c = bot.counters;
        say(('%s | casts %d bites %d catches %d releases %d lost %d breaks %d nothing %d skill-ups %d'):format(bot.state, c.casts, c.bites, c.catches, c.releases, c.lost, c.breaks, c.nothing, c.skillups));
    else
        cfg.window_open[1] = not cfg.window_open[1];
    end
end);

-- ---------------------------------------------------------------------------
-- window
-- ---------------------------------------------------------------------------
ashita.events.register('d3d_present', 'vanafish_present', function ()
    tick();
    if not cfg.window_open[1] then return; end
    imgui.SetNextWindowSize({ 360, 0 }, ImGuiCond_FirstUseEver);
    if imgui.Begin('Vanadreams fishing', cfg.window_open) then
        local c = bot.counters;
        if bot.running then
            if imgui.Button('Stop', { 120, 26 }) then stop('by button'); end
        else
            if imgui.Button('Start fishing', { 120, 26 }) then start(); end
        end
        imgui.SameLine();
        imgui.Text(bot.state .. (bot.stop_reason ~= '' and not bot.running and (': ' .. bot.stop_reason) or ''));
        if bot.running and bot.deadline > 0 then imgui.SameLine(); imgui.TextDisabled(('%ds'):format(math.max(0, bot.deadline - now()))); end
        imgui.Separator();
        local rod, bait = equipped_item(SLOT_RANGE), equipped_item(SLOT_AMMO);
        imgui.Text(('Rod: %s   Bait: %s%s'):format(rod and item_name(rod.Id) or 'none', bait and item_name(bait.Id) or 'none', bait and (' x' .. bait.Count) or ''));
        imgui.Text(('Skill: %.1f   Free slots: %d'):format(fishing_skill() / 10, free_slots()));
        imgui.Text(('Casts %d  Bites %d  Catches %d  Releases %d'):format(c.casts, c.bites, c.catches, c.releases));
        imgui.Text(('Lost %d  Breaks %d  Nothing %d  Skill-ups %d'):format(c.lost, c.breaks, c.nothing, c.skillups));
        if bot.fish then
            imgui.TextDisabled(('Last bite: %s%s  stamina %d  time %ds  regen %d'):format(bot.bite or '?', bot.feeling and (' (' .. bot.feeling .. ')') or '', bot.fish.stamina, bot.fish.time, bot.fish.regen));
        end
        if bot.last_result ~= '' then imgui.TextDisabled('Last: ' .. bot.last_result); end
        imgui.Separator();
        if imgui.CollapsingHeader('What to keep') then
            local changed = false;
            changed = imgui.Checkbox('Small fish', cfg.catch_small_fish) or changed;
            changed = imgui.Checkbox('Large fish', cfg.catch_large_fish) or changed;
            changed = imgui.Checkbox('Items', cfg.catch_items) or changed;
            changed = imgui.Checkbox('Monsters', cfg.catch_monsters) or changed;
            changed = imgui.Checkbox("Release when you don't have the skill", cfg.release_on_noskill) or changed;
            changed = imgui.SliderInt('Release anyway, % of bites', cfg.release_chance, 0, 50) or changed;
            if changed then settings.save(); end
        end
        if imgui.CollapsingHeader('Timing') then
            local changed = false;
            changed = imgui.SliderInt('Reel after, min s', cfg.reel_delay_min, 2, 60) or changed;
            changed = imgui.SliderInt('Reel after, max s', cfg.reel_delay_max, 2, 60) or changed;
            changed = imgui.SliderInt('Between casts, min s', cfg.cast_delay_min, 5, 60) or changed;
            changed = imgui.SliderInt('Between casts, max s', cfg.cast_delay_max, 5, 120) or changed;
            if changed then settings.save(); end
        end
        if imgui.CollapsingHeader('Stop when') then
            local changed = false;
            changed = imgui.SliderInt('Free slots at or below', cfg.stop_free_slots, 0, 10) or changed;
            changed = imgui.Checkbox('A tell arrives', cfg.stop_on_tell) or changed;
            changed = imgui.Checkbox('You move', cfg.stop_on_move) or changed;
            changed = imgui.InputInt('Casts (0 = no limit)', cfg.stop_max_casts) or changed;
            changed = imgui.InputInt('Minutes (0 = no limit)', cfg.stop_max_minutes) or changed;
            changed = imgui.InputInt('Skill reaches (0 = none)', cfg.stop_at_skill) or changed;
            changed = imgui.InputInt('Nothing bites, in a row', cfg.stop_nothing_streak) or changed;
            changed = imgui.InputText('Command when stopped', cfg.alert_command, 128) or changed;
            if changed then settings.save(); end
        end
    end
    imgui.End();
end);

ashita.events.register('load', 'vanafish_load', function ()
    check_server();
    if bot.allowed then say('loaded. /vanafish to open the window, /vanafish start to fish.');
    else say('loaded but idle: ' .. bot.allowed_reason); end
end);

ashita.events.register('unload', 'vanafish_unload', function ()
    if bot.running then stop('addon unloaded'); end
    settings.save();
end);
