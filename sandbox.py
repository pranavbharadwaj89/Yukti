"""
One-time setup script for test dependencies and execution sandbox.
"""
from bash_tool import install_test_dependencies

if __name__ == "__main__":
    print("Initializing test sandbox environment...")
    install_test_dependencies()
    print("Sandbox environment ready.")
