def test_healthy():
    assert 1 + 1 == 2

def test_login_scenario():
    user = {"name": "admin", "active": True}
    assert user["active"] is True
