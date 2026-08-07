from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any

from satli.catalog import CatalogRepository
from satli.cli_protocol import emit_jsonl, game_record, print_json
from satli.cli_validation import validate_app_ids
from satli.errors import CatalogError, UsageError
from satli.managed_games import ManagedGameRegistry
from satli.models import Catalog


def command_status(args: argparse.Namespace) -> int:
    if args.json and args.jsonl:
        raise UsageError("--json 与 --jsonl 不能同时使用")
    data_dir = Path(args.data_dir)
    registry = ManagedGameRegistry(data_dir)
    app_ids = validate_app_ids(args.app_ids or list(registry.managed_app_ids()))
    catalog: Catalog | None
    try:
        catalog = CatalogRepository(data_dir).load(offline=args.offline)
    except CatalogError:
        catalog = None
    records: list[dict[str, Any]] = []
    for app_id in app_ids:
        managed = registry.record(app_id)
        entry = catalog.entries.get(app_id) if catalog else None
        if entry:
            record = game_record(
                entry,
                [],
                managed.installed_state,
                "none",
                managed.installed_variant_id,
                managed.installed_source,
                managed.installed_at,
                managed.installed_sha256,
            )
        else:
            record = {
                "app_id": app_id,
                "game_name": managed.game_name or app_id,
                "discovery": [],
                "catalog_status": "unknown",
                "variants": [],
                "installed_state": managed.installed_state,
                "installed_variant_id": managed.installed_variant_id,
                "action": "none",
                "error": None,
            }
        record.update({
            "installed_source": managed.installed_source,
            "installed_at": managed.installed_at,
            "installed_sha256": managed.installed_sha256,
        })
        records.append(record)
    if args.jsonl:
        emit_jsonl("status", "plan", {"count": len(records)})
        for record in records:
            emit_jsonl("status", "item-succeeded", record)
        emit_jsonl("status", "completed", {"count": len(records), "exit_code": 0})
    elif args.json:
        print_json(records)
    elif records:
        for record in records:
            print(f"{record['app_id']:>10}  {record['installed_state']:<10} {record['game_name']}")
    else:
        print("没有 SATLI 管理的安装记录。")
    return 0
