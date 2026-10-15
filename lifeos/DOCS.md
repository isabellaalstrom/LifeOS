# LifeOS

Personal life management with goals, weekly sprints, habits and retrospectives.

- Open **LifeOS** from the sidebar. On first start, create the owner account (password at least 10 characters).
  LifeOS has its own login; it does not use your Home Assistant user.
- Data (database and keys) is stored in the app's persistent `/data` and is included in Home Assistant backups.
  Backups stop the app briefly so the database is consistent.
- LifeOS is only *hosted* by Home Assistant. It does not read or control any entities, APIs or automations.
