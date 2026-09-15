--[[
    crafty - repeat a synthesis on Vanadreams, the way Crafty did on Ashita v3.

    Runs only on Vanadreams. You give it a crystal and the ingredients once; it finds them in your
    inventory, sends the synthesis, waits for the result, waits out the server's cooldown, and goes
    again until the count is reached or something runs out.

    The usual way: craft the recipe once by hand through the game's menu. Crafty sees it. Open the
    window with /crafty, set how many times, press Repeat.

    Commands:
      /crafty                                   toggle the window
      /crafty repeat [count]                    repeat the synth you last did by hand
      /crafty add <count> "<crystal>" "<ingredient>" ["<ingredient>" ...]
                                                queue a recipe; list an ingredient twice to use two
      /crafty list                              show the queue
      /crafty clear                             empty the queue
      /crafty start                             start on the queue
      /crafty stop                              stop
      /crafty delay <seconds>                   seconds between synths (the server needs 15)

    Server side: a synthesis is packet 0x096 (crystal id and inventory slot, then the ingredient
    ids and slots); the result comes back in 0x06F (0 success, 1 failed, 2 interrupted, 3 bad
    recipe, 6 skill too low, 7 rare item held, 0xD wait longer). Ingredients must all be in the
    main inventory, and one slot may be used as many times as it holds.
]]

addon.name    = 'crafty';
addon.author  = 'Vanadreams';
addon.version = '0.1.0';
addon.desc    = 'Repeat a synthesis on the Vanadreams server.';
addon.link    = 'https://github.com/Finalferrin/vanadreams-ashita';

require('common');
local imgui    = require('imgui');
local settings = require('settings');

local defaults = T{
    window_open = T{ true },
    delay       = T{ 18 },      -- seconds between synths; the server refuses anything under 15
    stop_free_slots = T{ 1 },   -- stop when free inventory slots fall to this
    stop_on_fail_streak = T{ 8 },
    servers     = T{ 'vanadreams' },
    recipes     = T{ },         -- every synth you have done by hand, newest first: { crystal = id, ingredients = { id, ... } }
    see_through = T{ true },    -- window background: see-through so it can sit beside the crafting menu, or solid
};
local cfg = settings.load(defaults);

-- ---------------------------------------------------------------------------
-- state
-- ---------------------------------------------------------------------------
local S = { IDLE = 'idle', SENT = 'synthesizing', COOLDOWN = 'cooldown', STOPPED = 'stopped' };

local bot = {
    running = false, state = S.STOPPED, stop_reason = '', deadline = 0,
    queue = T{},            -- { count = n, done = n, crystal = id, ingredients = { id, id, ... } }
    last = nil,             -- the synth you last did by hand: { crystal = id, ingredients = { ... }, at = clock }
    selected = 1,           -- index into cfg.recipes shown in the dropdown
    next = nil,             -- the outcome of the synth in progress, known the moment it starts: { text, colour, at }
    repeat_count = T{ 10 },
    counters = T{ synths = 0, success = 0, hq = 0, failed = 0, other = 0 },
    fail_streak = 0, last_result = '', allowed = false, allowed_reason = '',
};

local function now() return os.clock(); end
local function say(msg) print(('\30\08[crafty]\30\01 %s'):format(msg)); end

local function stop(reason)
    if not bot.running then return; end
    bot.running = false; bot.stop_reason = reason or ''; bot.state = S.STOPPED; bot.deadline = 0;
    say('stopped: ' .. bot.stop_reason);
end

local function check_server()
    local cmd = '';
    pcall(function() cmd = AshitaCore:GetConfigurationManager():GetString('boot', 'ashita.boot', 'command') or ''; end);
    cmd = cmd:lower();
    for _, name in ipairs(cfg.servers) do
        if #name > 0 and cmd:find(name:lower(), 1, true) then bot.allowed = true; bot.allowed_reason = 'server: ' .. name; return true; end
    end
    bot.allowed = false; bot.allowed_reason = 'this addon runs on Vanadreams only (boot command does not name it)';
    return false;
end

-- ---------------------------------------------------------------------------
-- items
-- ---------------------------------------------------------------------------
local function item_name(id)
    local r = AshitaCore:GetResourceManager():GetItemById(id);
    return r and r.Name[1] or ('item ' .. tostring(id));
end

-- an id typed as a number, or a name looked up case-insensitively
local function resolve_item(text)
    local n = tonumber(text);
    if n then return n; end
    local rm = AshitaCore:GetResourceManager();
    local ok, r = pcall(function() return rm:GetItemByName(text, 0); end);
    if ok and r and r.Id and r.Id > 0 then return r.Id; end
    return nil;
end

local function free_slots()
    local inv = AshitaCore:GetMemoryManager():GetInventory();
    return inv:GetContainerCountMax(0) - inv:GetContainerCount(0);
end

-- every main-inventory slot holding this item: { { slot, count }, ... }
local function slots_of(id)
    local inv = AshitaCore:GetMemoryManager():GetInventory();
    local out = T{};
    for slot = 1, inv:GetContainerCountMax(0) do
        local it = inv:GetContainerItem(0, slot);
        if it and it.Id == id and it.Count > 0 then out:append({ slot = slot, count = it.Count }); end
    end
    return out;
end

-- pick a slot per ingredient use, spending a slot's quantity across repeated uses
local function plan(recipe)
    local crystal = slots_of(recipe.crystal)[1];
    if not crystal then return nil, 'no ' .. item_name(recipe.crystal) .. ' in your inventory'; end
    local remaining = {};
    local picks = T{};
    for _, id in ipairs(recipe.ingredients) do
        remaining[id] = remaining[id] or slots_of(id);
        local chosen = nil;
        for _, s in ipairs(remaining[id]) do
            if s.count > 0 then chosen = s; break; end
        end
        if not chosen then return nil, 'out of ' .. item_name(id); end
        chosen.count = chosen.count - 1;
        picks:append({ id = id, slot = chosen.slot });
    end
    return { crystal_slot = crystal.slot, picks = picks };
end

-- ---------------------------------------------------------------------------
-- the synthesis packet
-- ---------------------------------------------------------------------------
local function send_synth(recipe, p)
    local ids, slots = {}, {};
    for i = 1, 8 do
        ids[i] = p.picks[i] and p.picks[i].id or 0;
        slots[i] = p.picks[i] and p.picks[i].slot or 0;
    end
    local packet = struct.pack('<BBHBBHBBHHHHHHHHBBBBBBBBH',
        0x96, 0x12, 0,                      -- id, size in words, sync
        0, 0,                               -- HashNo, padding
        recipe.crystal, p.crystal_slot, #p.picks,
        ids[1], ids[2], ids[3], ids[4], ids[5], ids[6], ids[7], ids[8],
        slots[1], slots[2], slots[3], slots[4], slots[5], slots[6], slots[7], slots[8],
        0):totable();
    AshitaCore:GetPacketManager():AddOutgoingPacket(0x096, packet);
end

-- ---------------------------------------------------------------------------
-- the loop
-- ---------------------------------------------------------------------------
local function current_recipe()
    for _, r in ipairs(bot.queue) do if r.done < r.count then return r; end end
    return nil;
end

local function begin_synth()
    local recipe = current_recipe();
    if not recipe then stop('queue finished'); return; end
    if free_slots() <= cfg.stop_free_slots[1] then stop('inventory nearly full'); return; end
    local p, why = plan(recipe);
    if not p then stop(why); return; end
    send_synth(recipe, p);
    bot.counters.synths = bot.counters.synths + 1;
    bot.state = S.SENT; bot.deadline = now() + 60;
end

local function tick()
    if not bot.running then return; end
    if bot.state == S.SENT and now() > bot.deadline then stop('no result came back in a minute'); return; end
    if bot.state == S.COOLDOWN and now() >= bot.deadline then begin_synth(); end
end

local function start()
    if bot.running then say('already running'); return; end
    if not check_server() then say(bot.allowed_reason); return; end
    if not current_recipe() then say('nothing queued: /crafty add <count> "<crystal>" "<ingredient>" ...'); return; end
    bot.running = true; bot.stop_reason = ''; bot.fail_streak = 0;
    say('starting');
    -- if you just crafted by hand, the server's cooldown is still running; wait it out first
    if bot.last and bot.last.at and now() - bot.last.at < cfg.delay[1] then
        bot.state = S.COOLDOWN; bot.deadline = bot.last.at + cfg.delay[1];
    else
        begin_synth();
    end
end

-- "Fire Crystal + Lizard Skin, Distilled Water"
local function recipe_label(r)
    local names = T{}; for _, id in ipairs(r.ingredients) do names:append(item_name(id)); end
    return item_name(r.crystal) .. ' + ' .. table.concat(names, ', ');
end

local function same_recipe(a, b)
    if a.crystal ~= b.crystal or #a.ingredients ~= #b.ingredients then return false; end
    for i = 1, #a.ingredients do if a.ingredients[i] ~= b.ingredients[i] then return false; end end
    return true;
end

-- keep a hand synth in the saved list, newest first, no duplicates, twenty at most
local function remember(crystal, ingredients)
    local r = { crystal = crystal, ingredients = ingredients };
    for i, old in ipairs(cfg.recipes) do
        if same_recipe(old, r) then table.remove(cfg.recipes, i); break; end
    end
    table.insert(cfg.recipes, 1, r);
    while #cfg.recipes > 20 do table.remove(cfg.recipes); end
    bot.selected = 1;
    settings.save();
end

-- Watch the synths you do by hand through the game's own menu, so the window can repeat them.
ashita.events.register('packet_out', 'crafty_packet_out', function (e)
    if e.id ~= 0x096 or e.injected then return; end
    local crystal = struct.unpack('<H', e.data, 0x06 + 1);
    local items = struct.unpack('<B', e.data, 0x09 + 1);
    if crystal == 0 or items < 1 or items > 8 then return; end
    local ingredients = T{};
    for i = 0, items - 1 do
        ingredients:append((struct.unpack('<H', e.data, 0x0A + i * 2 + 1)));
    end
    bot.last = { crystal = crystal, ingredients = ingredients, at = now() };
    remember(crystal, ingredients);
    say(('saw you craft %s. Open /crafty to repeat it.'):format(recipe_label(bot.last)));
end);

-- repeat the recipe picked in the dropdown (or the one you last did by hand)
local function repeat_selected(count)
    local r = cfg.recipes[bot.selected] or bot.last;
    if not r then say('craft something by hand first, then repeat it'); return; end
    bot.queue = T{ { count = count, done = 0, crystal = r.crystal, ingredients = r.ingredients } };
    start();
end

-- Vanadreams palette, from the site: night, plum, velvet, gold, soft gold, cream
local C = {
    night  = { 0.059, 0.039, 0.090 },
    plum   = { 0.137, 0.098, 0.220 },
    velvet = { 0.200, 0.133, 0.290 },
    gold   = { 0.839, 0.655, 0.298 },
    soft   = { 0.945, 0.804, 0.471 },
    cream  = { 1.000, 0.953, 0.824 },
    fail   = { 0.900, 0.450, 0.450 },
};
local function rgba(c, a) return { c[1], c[2], c[3], a }; end

-- The server decides the outcome when the synth starts and sends it in the animation packet,
-- so this knows the result while the crystal is still spinning.
local OUTCOME = {
    [0] = { 'FAIL',          C.fail },
    [1] = { 'Success',       C.soft },
    [2] = { 'HQ',            C.cream },
    [3] = { 'HQ +2',         C.cream },
    [4] = { 'HQ +3',         C.cream },
};

ashita.events.register('packet_in', 'crafty_effect_in', function (e)
    if e.id ~= 0x030 then return; end
    local who = struct.unpack('<I', e.data, 0x04 + 1);
    local me = AshitaCore:GetMemoryManager():GetParty():GetMemberServerId(0);
    if who ~= me then return; end
    local kind = struct.unpack('<b', e.data, 0x0C + 1);
    local o = OUTCOME[kind];
    if not o then return; end
    bot.next = { text = o[1], colour = o[2], at = now() };
    say('this synth: ' .. o[1]);
end);

local RESULT = { [0] = 'success', [1] = 'failed', [2] = 'interrupted', [3] = 'bad recipe', [4] = 'cancelled', [6] = 'skill too low', [7] = 'rare item held', [12] = 'desynth success', [13] = 'wait longer', [14] = 'interrupted' };

ashita.events.register('packet_in', 'crafty_packet_in', function (e)
    if e.id ~= 0x06F or not bot.running or bot.state ~= S.SENT then return; end
    local result, grade, count, _, item = struct.unpack('<BbBBH', e.data, 0x05);
    local recipe = current_recipe();
    local word = RESULT[result] or ('result ' .. tostring(result));
    if result == 0 or result == 12 then
        recipe.done = recipe.done + 1;
        bot.counters.success = bot.counters.success + 1;
        if grade and grade > 0 then bot.counters.hq = bot.counters.hq + 1; end
        bot.fail_streak = 0;
        bot.last_result = ('%s: %s x%d%s'):format(word, item_name(item), count or 1, (grade and grade > 0) and (' HQ' .. grade) or '');
    elseif result == 1 or result == 2 or result == 14 then
        recipe.done = recipe.done + 1;   -- the crystal is spent either way
        bot.counters.failed = bot.counters.failed + 1;
        bot.fail_streak = bot.fail_streak + 1;
        bot.last_result = word;
    else
        bot.counters.other = bot.counters.other + 1;
        bot.last_result = word;
        if result == 3 or result == 6 or result == 7 then stop(word); return; end
    end
    say(bot.last_result);
    if cfg.stop_on_fail_streak[1] > 0 and bot.fail_streak >= cfg.stop_on_fail_streak[1] then stop('too many failures in a row'); return; end
    bot.state = S.COOLDOWN;
    bot.deadline = now() + math.max(15, cfg.delay[1]) + math.random() * 3;
end);

-- ---------------------------------------------------------------------------
-- commands
-- ---------------------------------------------------------------------------
ashita.events.register('command', 'crafty_cmd', function (e)
    local args = e.command:args();
    if #args == 0 or args[1] ~= '/crafty' then return; end
    e.blocked = true;
    local sub = (args[2] or ''):lower();
    if sub == 'add' then
        local count = tonumber(args[3]);
        if not count or count < 1 or #args < 5 then say('usage: /crafty add <count> "<crystal>" "<ingredient>" ["<ingredient>" ...]'); return; end
        local crystal = resolve_item(args[4]);
        if not crystal then say('no item called ' .. args[4]); return; end
        local ingredients = T{};
        for i = 5, math.min(#args, 12) do
            local id = resolve_item(args[i]);
            if not id then say('no item called ' .. args[i]); return; end
            ingredients:append(id);
        end
        bot.queue:append({ count = count, done = 0, crystal = crystal, ingredients = ingredients });
        local names = T{}; for _, id in ipairs(ingredients) do names:append(item_name(id)); end
        say(('queued %d x %s + %s'):format(count, item_name(crystal), table.concat(names, ', ')));
    elseif sub == 'list' then
        if #bot.queue == 0 then say('queue is empty'); end
        for i, r in ipairs(bot.queue) do
            local names = T{}; for _, id in ipairs(r.ingredients) do names:append(item_name(id)); end
            say(('%d. %d/%d  %s + %s'):format(i, r.done, r.count, item_name(r.crystal), table.concat(names, ', ')));
        end
    elseif sub == 'clear' then bot.queue = T{}; say('queue cleared');
    elseif sub == 'start' then start();
    elseif sub == 'repeat' then repeat_selected(tonumber(args[3]) or bot.repeat_count[1]);
    elseif sub == 'stop' then stop('by command');
    elseif sub == 'delay' then
        local d = tonumber(args[3]);
        if d then cfg.delay[1] = math.max(15, d); settings.save(); say('delay ' .. cfg.delay[1] .. 's'); end
    else
        cfg.window_open[1] = not cfg.window_open[1];
    end
end);

-- ---------------------------------------------------------------------------
-- window
-- ---------------------------------------------------------------------------
ashita.events.register('d3d_present', 'crafty_present', function ()
    tick();
    if not cfg.window_open[1] then return; end
    imgui.SetNextWindowSize({ 360, 0 }, ImGuiCond_FirstUseEver);
    -- see-through, in the Vanadreams colours, so it can sit beside the crafting menu
    local see = cfg.see_through[1];
    imgui.PushStyleVar(ImGuiStyleVar_WindowRounding, 6.0);
    imgui.PushStyleColor(ImGuiCol_WindowBg,      rgba(C.night, see and 0.55 or 0.97));
    imgui.PushStyleColor(ImGuiCol_TitleBg,       rgba(C.plum, see and 0.70 or 1.0));
    imgui.PushStyleColor(ImGuiCol_TitleBgActive, rgba(C.velvet, see and 0.85 or 1.0));
    imgui.PushStyleColor(ImGuiCol_Border,        rgba(C.gold, 0.35));
    imgui.PushStyleColor(ImGuiCol_FrameBg,       rgba(C.velvet, 0.60));
    imgui.PushStyleColor(ImGuiCol_PopupBg,       rgba(C.night, 0.92));
    imgui.PushStyleColor(ImGuiCol_Button,        rgba(C.velvet, 0.85));
    imgui.PushStyleColor(ImGuiCol_ButtonHovered, rgba(C.gold, 0.60));
    imgui.PushStyleColor(ImGuiCol_ButtonActive,  rgba(C.gold, 0.85));
    imgui.PushStyleColor(ImGuiCol_Header,        rgba(C.velvet, 0.85));
    imgui.PushStyleColor(ImGuiCol_HeaderHovered, rgba(C.gold, 0.45));
    imgui.PushStyleColor(ImGuiCol_Separator,     rgba(C.gold, 0.35));
    imgui.PushStyleColor(ImGuiCol_Text,          rgba(C.cream, 1.0));
    imgui.PushStyleColor(ImGuiCol_TextDisabled,  rgba(C.soft, 0.75));
    if imgui.Begin('Vanadreams crafting', cfg.window_open) then
        local c = bot.counters;
        -- the synth in progress: the server already told us how it ends
        if bot.next and now() - bot.next.at < 20 then
            imgui.TextDisabled('This synth: ');
            imgui.SameLine();
            imgui.TextColored(rgba(bot.next.colour, 1.0), bot.next.text);
        else
            imgui.TextDisabled('This synth: shows here the moment it starts');
        end
        imgui.SameLine(imgui.GetWindowWidth() - 92);
        if imgui.SmallButton(see and 'solid' or 'see-through') then cfg.see_through[1] = not see; settings.save(); end
        imgui.Separator();
        -- the dropdown of every synth you have done by hand, newest first
        if #cfg.recipes == 0 then
            imgui.TextDisabled('Craft something once by hand and it shows up here.');
        else
            if bot.selected > #cfg.recipes then bot.selected = 1; end
            imgui.PushItemWidth(-1);
            if imgui.BeginCombo('##recipe', recipe_label(cfg.recipes[bot.selected])) then
                for i, r in ipairs(cfg.recipes) do
                    if imgui.Selectable(recipe_label(r) .. '##' .. i, i == bot.selected) then bot.selected = i; end
                end
                imgui.EndCombo();
            end
            imgui.PopItemWidth();
        end
        imgui.PushItemWidth(80);
        imgui.InputInt('times', bot.repeat_count);
        imgui.PopItemWidth();
        if bot.repeat_count[1] < 1 then bot.repeat_count[1] = 1; end
        imgui.SameLine();
        if bot.running then
            if imgui.Button('Stop', { 100, 24 }) then stop('by button'); end
        else
            if imgui.Button('Repeat', { 100, 24 }) then repeat_selected(bot.repeat_count[1]); end
        end
        if #cfg.recipes > 0 and not bot.running then
            imgui.SameLine();
            if imgui.Button('Forget', { 70, 24 }) then table.remove(cfg.recipes, bot.selected); bot.selected = 1; settings.save(); end
        end
        imgui.Separator();
        -- what it is doing right now
        local r = current_recipe();
        if bot.running and r then
            imgui.Text(('%s  %d/%d'):format(bot.state, r.done, r.count));
            if bot.deadline > 0 then imgui.SameLine(); imgui.TextDisabled(('next in %ds'):format(math.max(0, bot.deadline - now()))); end
        elseif bot.stop_reason ~= '' then
            imgui.TextDisabled('Stopped: ' .. bot.stop_reason);
        else
            imgui.TextDisabled('Idle');
        end
        imgui.Text(('Synths %d   Success %d   HQ %d   Failed %d   Free slots %d'):format(c.synths, c.success, c.hq, c.failed, free_slots()));
        if bot.last_result ~= '' then imgui.TextDisabled('Last: ' .. bot.last_result); end
        if imgui.CollapsingHeader('Settings') then
            local changed = false;
            changed = imgui.InputInt('Seconds between synths', cfg.delay) or changed;
            changed = imgui.InputInt('Stop at free slots', cfg.stop_free_slots) or changed;
            changed = imgui.InputInt('Stop after failures in a row (0 = never)', cfg.stop_on_fail_streak) or changed;
            if changed then if cfg.delay[1] < 15 then cfg.delay[1] = 15; end settings.save(); end
        end
        if not bot.allowed and bot.allowed_reason ~= '' then imgui.TextDisabled(bot.allowed_reason); end
    end
    imgui.End();
    imgui.PopStyleColor(14);
    imgui.PopStyleVar(1);
end);

ashita.events.register('load', 'crafty_load', function ()
    check_server();
    say(bot.allowed and 'loaded. Craft once by hand, then /crafty and press Repeat.' or bot.allowed_reason);
end);
