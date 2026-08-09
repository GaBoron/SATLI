from __future__ import annotations

import argparse

from satli.cli_protocol import emit_jsonl
from satli.cli_validation import confirm, validate_app_ids
from satli.file_protection import is_read_only, set_read_only
from satli.steam import find_steam_dir, schema_target


def command_protect(args: argparse.Namespace) -> int:
    app_ids = validate_app_ids(args.app_ids)
    enable = args.protection_command == "lock"
    if enable:
        confirm(
            "巨大风险：只读属性会影响 Steam 更新、校验和缓存重建，且不能保证 Steam 不覆盖文件。继续强制锁定？",
            args.yes,
        )
    steam_dir = find_steam_dir(args.steam_dir)
    if args.jsonl:
        emit_jsonl(
            "protect",
            "plan",
            {"action": args.protection_command, "count": len(app_ids)},
        )
    for position, app_id in enumerate(app_ids, 1):
        target = schema_target(steam_dir, app_id)
        if args.jsonl:
            emit_jsonl(
                "protect",
                "item-started",
                {"app_id": app_id, "position": position},
            )
        set_read_only(target, enable)
        record = {
            "app_id": app_id,
            "target": str(target),
            "file_read_only": is_read_only(target),
            "action": "locked" if enable else "unlocked",
            "position": position,
        }
        if args.jsonl:
            emit_jsonl("protect", "item-succeeded", record)
        else:
            print(f"{app_id}：{'已强制锁定（只读）' if enable else '已解除只读锁定'}")
    if args.jsonl:
        emit_jsonl("protect", "completed", {"count": len(app_ids), "exit_code": 0})
    return 0
