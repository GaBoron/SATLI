local logger = require("logger")
local millennium = require("millennium")

local EMPTY_BRIDGE = '{"version":1,"generated_at":"","apps":{}}'
local MAXIMUM_BRIDGE_BYTES = 32 * 1024 * 1024
local last_heartbeat = 0

local function bridge_path()
    local install_path = millennium.get_install_path()
    if install_path == nil or install_path == "" then
        return nil
    end
    return install_path .. "\\config\\satli-bridge-v1.json"
end

local function write_heartbeat()
    local now = os.time()
    if now - last_heartbeat < 5 then
        return
    end
    local install_path = millennium.get_install_path()
    if install_path == nil or install_path == "" then
        return
    end
    local path = install_path .. "\\config\\satli-runtime-v1.json"
    local file = io.open(path, "wb")
    if file ~= nil then
        file:write(string.format('{"version":1,"heartbeat_unix":%d}\n', now))
        file:close()
        last_heartbeat = now
    end
end

---@ffi
---@return string
function getBridgeSnapshot()
    write_heartbeat()
    local path = bridge_path()
    if path == nil then
        return EMPTY_BRIDGE
    end
    local file = io.open(path, "rb")
    if file == nil then
        return EMPTY_BRIDGE
    end
    local size = file:seek("end")
    if size == nil or size > MAXIMUM_BRIDGE_BYTES then
        file:close()
        logger:error("Rejected invalid SATLI bridge size")
        return EMPTY_BRIDGE
    end
    file:seek("set", 0)
    local payload = file:read("*a")
    file:close()
    return payload or EMPTY_BRIDGE
end

logger:info("Registered SATLI bridge RPC")
