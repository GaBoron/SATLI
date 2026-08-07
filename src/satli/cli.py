from __future__ import annotations

import argparse
import sys
from typing import Sequence

from satli.cli_arguments import build_parser
from satli.cli_protocol import emit_jsonl
from satli.data_paths import local_app_data_root, migrate_default_data_dir
from satli.errors import SatliError


def main(argv: Sequence[str] | None = None) -> int:
    raw_argv = list(sys.argv[1:] if argv is None else argv)
    if (
        argv is None
        and "--data-dir" not in raw_argv
        and not any(value in {"--help", "-h", "--version"} for value in raw_argv)
    ):
        migrate_default_data_dir(local_app_data_root())
    parser = build_parser()
    args: argparse.Namespace | None = None
    try:
        args = parser.parse_args(raw_argv)
        return int(args.handler(args))
    except SatliError as exc:
        if args is not None and getattr(args, "jsonl", False):
            operation = {
                "cache": "cache-refresh",
                "petition": "petition-export",
                "schema": f"schema-{getattr(args, 'schema_command', 'unknown')}",
            }.get(getattr(args, "command", None), str(args.command))
            emit_jsonl(operation, "error", {"message": str(exc), "exit_code": exc.exit_code})
        else:
            print(f"错误：{exc}", file=sys.stderr)
        return exc.exit_code
    except KeyboardInterrupt:
        print("已取消。", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
