#!/usr/bin/env python3
"""
Root runner for Jira ticket scenario analysis.
Delegates to sarvaghna/integrations/jira/run.py
"""
import os
import sys

jira_integration_dir = os.path.join(os.path.dirname(__file__), "sarvaghna", "integrations", "jira")
sys.path.insert(0, jira_integration_dir)

from run import analyse_ticket

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python run.py <TICKET_ID> (e.g. python run.py PROJ-123)")
        sys.exit(1)
    analyse_ticket(sys.argv[1])
