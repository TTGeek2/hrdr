# Hrdr

Self-hosted feature/bug tracker for ideas you want to implement later. .NET 10 + SQLite, minimal Razor UI, MCP tools for LLM CRUD.

## Features

- **Projects** with name and slug
- **Work items** (`Feature` | `Bug`) with title, description, numeric ID, project relation
- Overview filter by project (and type)
- Drag-and-drop priority within a project
- MCP at `/mcp` for agent CRUD

## Local run

```bash
dotnet run --project src/Hrdr --urls http://localhost:5080
```

UI: http://localhost:5080  
API: http://localhost:5080/api/…  
MCP: http://localhost:5080/mcp  

SQLite defaults to `src/Hrdr/data/hrdr.db`.

## Deploy on pi501

Host prep (already done once): .NET 10 at `~/.dotnet`, dirs under `~/Projects/hrdr`.

From this machine:

```bash
rsync -az --delete --exclude bin --exclude obj --exclude data --exclude .git \
  ./ ttgeek@pi501.local:Projects/hrdr/src/

ssh ttgeek@pi501.local 'export PATH=$PATH:$HOME/.dotnet
  cd ~/Projects/hrdr/src
  dotnet publish src/Hrdr/Hrdr.csproj -c Release -o ~/Projects/hrdr/publish
  mkdir -p ~/.config/systemd/user ~/Projects/hrdr/data
  cp ~/Projects/hrdr/src/deploy/hrdr.env ~/Projects/hrdr/hrdr.env
  cp ~/Projects/hrdr/src/deploy/hrdr.service ~/.config/systemd/user/hrdr.service
  systemctl --user daemon-reload
  systemctl --user enable --now hrdr.service
  loginctl enable-linger $USER'
```

App: http://pi501.local:5080  
DB: `/home/ttgeek/Projects/hrdr/data/hrdr.db`

## Cursor MCP config

Add to Cursor MCP settings (see also `docs/mcp-cursor-config.json`):

```json
{
  "mcpServers": {
    "hrdr": {
      "url": "http://pi501.local:5080/mcp"
    }
  }
}
```

### MCP tools

| Tool | Purpose |
|------|---------|
| `list_projects` / `create_project` / `update_project` / `delete_project` | Project CRUD |
| `list_items` / `get_item` / `create_item` / `update_item` / `delete_item` | Item CRUD (+ project/type filter on list) |
| `reorder_items` | Set priority order for a project |

## Tests

```bash
dotnet test
```
