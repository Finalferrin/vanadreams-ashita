--[[
    vanachatfix - loads chatfix only on the servers that need it.

    Ashita ships an addon called chatfix for private servers whose code is older than a
    client update that moved the chat message one byte along in the chat packet. On those
    servers chat is unreadable without it. On a server that already sends the current
    layout - Vanadreams does - chatfix does harm instead: every chat line loses its first
    letter, tells go to the wrong name, and the pop-up menus NPCs open are cut up at the
    spaces.

    This addon rewrites nothing itself. It looks at the first chat packet the server
    sends, tells the two layouts apart by one byte, and then either loads the stock
    chatfix (old layout) or makes sure it is not loaded (current layout).

    How it tells: the chat packet carries the speaker's name and then the message. In the
    current layout the message starts at byte 23, so that byte is its first letter and is
    never zero. In the old layout the name field is one byte longer and names are at most
    fifteen letters, so byte 23 is the zero that ends the name and the message starts at
    byte 24. The old-layout half of that is read from what stock chatfix does, not tested
    against such a server.

    Commands:
      /vanachatfix         say what was decided for this server
]]

addon.name    = 'vanachatfix';
addon.author  = 'Vanadreams';
addon.version = '0.1.0';
addon.desc    = 'Loads chatfix only on servers that still send the old chat packet layout.';
addon.link    = 'https://github.com/Finalferrin/vanadreams-ashita';

require('common');

local CHAT_PACKET = 0x0017;
local decided     = nil;   -- 'current' | 'old', once the first usable chat packet has been seen

-- The whole decision, on its own so it can be read and checked without the game.
-- b23 and b24 are the bytes at offsets 23 and 24 of the packet as the server sent it.
local function layout_of(b23, b24)
    if b23 ~= 0 then return 'current'; end
    if b24 ~= 0 then return 'old'; end
    return nil;   -- an empty message says nothing either way; wait for the next one
end

local function say(text)
    print(('[vanachatfix] %s'):format(text));
end

ashita.events.register('packet_in', 'vanachatfix_in', function (e)
    if decided ~= nil or e.id ~= CHAT_PACKET or e.size < 25 then return; end

    -- e.data is the packet as it arrived, before any other addon has touched it
    local b23, b24 = struct.unpack('BB', e.data, 23 + 1);
    local layout = layout_of(b23, b24);
    if layout == nil then return; end
    decided = layout;

    if layout == 'old' then
        AshitaCore:GetChatManager():QueueCommand(1, '/addon load chatfix');
        say('this server sends the old chat layout: chatfix loaded.');
    else
        AshitaCore:GetChatManager():QueueCommand(1, '/addon unload chatfix');
    end
end);

ashita.events.register('command', 'vanachatfix_command', function (e)
    local args = e.command:args();
    if #args == 0 or args[1]:lower() ~= '/vanachatfix' then return; end
    e.blocked = true;

    if decided == nil then
        say('no chat packet seen yet, so nothing decided.');
    elseif decided == 'old' then
        say('old chat layout here: chatfix is loaded.');
    else
        say('current chat layout here: chatfix is kept off.');
    end
end);
