local logger = require("logger")
local millennium = require("millennium")

require("rpc_functions")

local function on_load()
    logger:info("SATLI display bridge backend loaded")
    millennium.ready()
end

local function on_frontend_loaded()
    logger:info("SATLI display bridge frontend loaded")
end

local function on_unload()
    logger:info("SATLI display bridge unloaded")
end

return {
    on_load = on_load,
    on_frontend_loaded = on_frontend_loaded,
    on_unload = on_unload,
}
