#!/usr/bin/env python3
"""
redact_secrets.py — manual YAML-secret scrubber.

⚠ This script is NOT wired as a pre-commit hook (see .pre-commit-config.yaml
for why). Its `is_secret` heuristic is intentionally aggressive — it
matches *any* field whose key contains "password", "secret", "key", or
"token" — which makes it dangerous to run automatically. Use it manually
when you intentionally want to scrub a file in place:

    python .hooks/redact_secrets.py path/to/values.yaml

Requires `ruamel.yaml` (pip install ruamel.yaml).
"""
import sys
import re
import ruamel.yaml
from pathlib import Path

def is_secret(value):
    """Heuristic: check for common password patterns or high-entropy strings."""
    if not isinstance(value, str):
        return False
    value = value.strip()
    # Common password/API key fields
    password_patterns = [
        r'(?i)(password|passwd|pwd|pass|secret|key|token|api[_-]?key|access[_-]?key)',
        r'^[A-Za-z0-9+/]{20,}$',  # Base64-like secrets
        r'^[0-9a-f]{32,}$',       # Hex keys
        r'(?i)bearer\s+[A-Za-z0-9_-]+',  # Bearer tokens
    ]
    for pattern in password_patterns:
        if re.search(pattern, value):
            return True
    return False

def redact_yaml(data, yaml):
    """Recursively find and redact secret scalar values."""
    if isinstance(data, dict):
        for k, v in list(data.items()):
            if isinstance(v, (str, ruamel.yaml.scalarstring.SingleQuotedScalarString, ruamel.yaml.scalarstring.DoubleQuotedScalarString)):
                if is_secret(v):
                    print(f"Redacting secret in key '{k}': {repr(v)[:50]}...", file=sys.stderr)
                    data[k] = '${SECRET_PLACEHOLDER}'  # Or f"${k.upper()}"
            else:
                redact_yaml(v, yaml)
    elif isinstance(data, list):
        for i, item in enumerate(data):
            redact_yaml(item, yaml)

def main():
    if len(sys.argv) < 2:
        print("Usage: python redact_secrets.py <file>", file=sys.stderr)
        sys.exit(1)

    yaml = ruamel.yaml.YAML()
    for filepath in sys.argv[1:]:
        path = Path(filepath)
        if not path.is_file() or path.suffix.lower() not in ('.yaml', '.yml'):
            continue

        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = yaml.load(f)

            redact_yaml(data, yaml)

            with open(path, 'w', encoding='utf-8') as f:
                yaml.dump(data, f)

            print(f"Processed {filepath}", file=sys.stderr)
        except Exception as e:
            print(f"Error processing {filepath}: {e}", file=sys.stderr)
            sys.exit(1)

    sys.exit(0)

if __name__ == '__main__':
    main()
