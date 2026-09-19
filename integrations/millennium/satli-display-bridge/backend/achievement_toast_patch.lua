-- Translate the achievement record built from the notification protobuf before
-- React renders desktop, in-game or Big Picture notifications.
-- GameSessions callbacks only cover a separate library activity path.
return {
    {
        file = [[chunk~[0-9a-f]+\.js]],
        find = [[\w+=\{strID:\w+\.data\.achievement_id\(\).*?flMaxProgress:\w+\.data\.max_progress\(\)\}]],
        transforms = {
            {
                match = [[(\w+)=(\{strID:(\w+)\.data\.achievement_id\(\).*?flMaxProgress:\w+\.data\.max_progress\(\)\})]],
                replace = [[\1=(#{{self}}?.achievementToast?.translate?.(\3.data.appid(),\2)??\2)]],
            },
        },
    },
}
