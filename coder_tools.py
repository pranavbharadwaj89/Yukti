"""
Helper tools for Coder agent inspection and generation.
"""
from pathlib import Path

def list_test_files(directory: str = "output_tests") -> list[str]:
    d = Path(directory)
    if not d.exists():
        return []
    return [str(f) for f in d.glob("test_*.py")]
