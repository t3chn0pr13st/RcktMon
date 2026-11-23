#!/bin/bash
# Backward-compatible command. Studio is now a page of the full RocketMon application.
exec "$(dirname "$0")/install-desktop.sh" "$@"
