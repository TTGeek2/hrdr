(function () {
    var clientKey = 'hrdr.liveClientId';
    var clientId = sessionStorage.getItem(clientKey);
    if (!clientId) {
        clientId = (window.crypto && typeof crypto.randomUUID === 'function')
            ? crypto.randomUUID()
            : 'c-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
        sessionStorage.setItem(clientKey, clientId);
    }

    var originalFetch = window.fetch.bind(window);
    window.fetch = function (input, init) {
        if (typeof input !== 'string' && !(input instanceof URL))
            return originalFetch(input, init);
        var url = input instanceof URL ? input.href : input;
        if (!isSameOrigin(url))
            return originalFetch(input, init);
        var next = init ? Object.assign({}, init) : {};
        var headers = new Headers(next.headers || undefined);
        headers.set('X-Hrdr-Client', clientId);
        next.headers = headers;
        return originalFetch(input, next);
    };

    if (typeof signalR === 'undefined') {
        console.warn('Live updates are unavailable.');
        return;
    }

    var kinds = [
        'WorkItemCreated',
        'WorkItemUpdated',
        'WorkItemDeleted',
        'WorkItemsReordered',
        'ProjectCreated',
        'ProjectUpdated',
        'ProjectDeleted',
        'CommentCreated',
        'CommentDeleted',
        'BoardColumnsReordered'
    ];
    var statesByNumber = {
        0: 'Created',
        1: 'Refined',
        2: 'In development',
        3: 'on pullrequest',
        4: 'done',
        5: 'released'
    };
    var stateAliases = {
        'created': 'Created',
        'refined': 'Refined',
        'indevelopment': 'In development',
        'in development': 'In development',
        'onpullrequest': 'on pullrequest',
        'on pullrequest': 'on pullrequest',
        'done': 'done',
        'released': 'released'
    };

    var deferred = [];
    var reloading = false;

    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/hrdr')
        .withAutomaticReconnect()
        .build();

    connection.on('boardChanged', onBoardChanged);
    connection.onreconnected(function () {
        var active = document.activeElement;
        var typing = active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.tagName === 'SELECT');
        if (!typing) reload();
    });
    connection.start().catch(function (err) {
        console.warn('Live updates are unavailable.', err);
    });

    ['pointerup', 'pointercancel', 'mouseup', 'touchend'].forEach(function (type) {
        document.addEventListener(type, function () {
            setTimeout(flushDeferred, 0);
        }, true);
    });

    function onBoardChanged(raw) {
        var change = normalizeChange(raw || {});
        if (change.originClientId && change.originClientId === clientId) return;
        if (isDragging()) {
            deferred.push(change);
            return;
        }
        apply(change);
    }

    function flushDeferred() {
        if (reloading || isDragging() || deferred.length === 0) return;
        var batch = deferred.splice(0, deferred.length);
        batch.forEach(apply);
    }

    function apply(change) {
        if (reloading) return;
        switch (change.kind) {
            case 'WorkItemCreated':
            case 'WorkItemUpdated':
                if (!change.item) { reload(); return; }
                upsertItem(change.item);
                break;
            case 'WorkItemDeleted':
                removeItem(change.workItemId);
                break;
            case 'WorkItemsReordered':
                if (!change.orderedIds || !change.orderedIds.length) { reload(); return; }
                reorderItems(change.projectId, change.orderedIds);
                break;
            case 'ProjectCreated':
            case 'ProjectUpdated':
                if (!change.project) {
                    if (pageShowsProjects()) reload();
                    return;
                }
                applyProject(change.project);
                break;
            case 'ProjectDeleted':
                removeProject(change.projectId);
                break;
            case 'CommentCreated':
                if (!change.comment) {
                    if (commentsRoot()) reload();
                    return;
                }
                addComment(change.comment);
                break;
            case 'CommentDeleted':
                removeComment(change.commentId, change.workItemId);
                break;
            case 'BoardColumnsReordered':
                if (!change.orderedStates || !change.orderedStates.length) { reload(); return; }
                reorderColumns(change.orderedStates);
                break;
            default:
                break;
        }
    }

    function upsertItem(item) {
        var board = document.querySelector('.kanban-board');
        if (!board) return;
        var existing = findCard(item.id);
        if (!itemMatchesFilters(item)) {
            if (existing) existing.remove();
            updateCounts();
            refreshOrderedIdsFromDom();
            syncEmpty();
            return;
        }
        var card = existing || createCard(item);
        updateCard(card, item);
        placeCard(card, item);
        updateCounts();
        refreshOrderedIdsFromDom();
        syncEmpty();
    }

    function removeItem(id) {
        if (onEditItem(id)) {
            go('/Items');
            return;
        }
        var card = findCard(id);
        if (card) card.remove();
        updateCounts();
        refreshOrderedIdsFromDom();
        syncEmpty();
    }

    function reorderItems(projectId, orderedIds) {
        var board = document.querySelector('.kanban-board');
        if (!board) return;
        var rank = new Map();
        orderedIds.forEach(function (id, index) { rank.set(Number(id), index); });
        board.querySelectorAll('.kanban-column').forEach(function (column) {
            var list = column.querySelector('.kanban-cards');
            if (!list) return;
            var cards = cardChildren(list);
            cards.forEach(function (card) {
                if (Number(card.dataset.projectId) !== Number(projectId)) return;
                var index = rank.get(Number(card.dataset.id));
                if (index != null) card.dataset.sortOrder = String(index);
            });
            cards.sort(function (a, b) { return compareKeys(cardKey(a), cardKey(b)); });
            cards.forEach(function (card) { list.appendChild(card); });
        });
        refreshOrderedIdsFromDom();
    }

    function reorderColumns(states) {
        var board = document.querySelector('.kanban-board');
        if (!board) return;
        var columns = Array.from(board.children).filter(function (el) {
            return el.classList && el.classList.contains('kanban-column') && el.hasAttribute('data-state');
        });
        var byState = new Map(columns.map(function (col) { return [col.getAttribute('data-state'), col]; }));
        if (states.length !== columns.length || states.some(function (state) { return !byState.has(state); })) {
            reload();
            return;
        }
        states.forEach(function (state) { board.appendChild(byState.get(state)); });
    }

    function applyProject(project) {
        projectSelects().forEach(function (select) { upsertProjectOption(select, project); });
        document.querySelectorAll('.kanban-card').forEach(function (card) {
            if (Number(card.dataset.projectId) !== Number(project.id)) return;
            var label = card.querySelector('.item-project');
            if (label) label.textContent = project.name || '';
        });
        upsertProjectRow(project);
    }

    function removeProject(projectId) {
        var id = String(projectId);
        var editProject = document.querySelector('select[name="Input.ProjectId"]');
        if (commentsRoot() && editProject && editProject.value === id) {
            go('/Items');
            return;
        }
        var boardProject = document.querySelector('.filters select[name="ProjectId"]');
        if (boardProject && boardProject.value === id) {
            reload();
            return;
        }
        projectSelects().forEach(function (select) {
            Array.from(select.options).forEach(function (option) {
                if (option.value === id) option.remove();
            });
        });
        document.querySelectorAll('.kanban-card').forEach(function (card) {
            if (card.dataset.projectId === id) card.remove();
        });
        updateCounts();
        refreshOrderedIdsFromDom();
        syncEmpty();
        var row = findProjectRow(projectId);
        if (row) row.remove();
        var list = document.querySelector('.project-list');
        if (list && list.children.length === 0) {
            list.remove();
            if (!projectsEmptyMessage()) {
                var form = document.querySelector('form.create-row');
                var note = document.createElement('p');
                note.className = 'empty';
                note.textContent = 'No projects yet. Create one to get started.';
                if (form) form.after(note);
            }
        }
    }

    function addComment(comment) {
        var root = commentsRoot();
        if (!root || String(root.dataset.workItemId) !== String(comment.workItemId)) return;
        if (root.querySelector('li[data-comment-id="' + cssEscape(String(comment.id)) + '"]')) return;
        var empty = Array.from(root.children).find(function (el) {
            return el.tagName === 'P' && el.classList.contains('empty');
        });
        if (empty) empty.remove();
        var list = Array.from(root.children).find(function (el) { return el.classList.contains('comment-list'); });
        if (!list) {
            list = document.createElement('ul');
            list.className = 'comment-list';
            var form = root.querySelector('.comment-form');
            if (form) root.insertBefore(list, form);
            else root.appendChild(list);
        }
        var li = createCommentItem(comment);
        var before = Array.from(list.children).find(function (el) {
            return Number(el.dataset.commentId) > Number(comment.id);
        });
        if (before) list.insertBefore(li, before);
        else list.appendChild(li);
    }

    function removeComment(commentId, workItemId) {
        var root = commentsRoot();
        if (!root) return;
        if (workItemId != null && String(root.dataset.workItemId) !== String(workItemId)) return;
        var li = root.querySelector('li[data-comment-id="' + cssEscape(String(commentId)) + '"]');
        if (!li) return;
        var list = li.parentElement;
        li.remove();
        if (list && list.classList.contains('comment-list') && list.children.length === 0) {
            list.remove();
            if (!Array.from(root.children).some(function (el) { return el.tagName === 'P' && el.classList.contains('empty'); })) {
                var note = document.createElement('p');
                note.className = 'empty';
                note.textContent = 'No comments yet.';
                var form = root.querySelector('.comment-form');
                if (form) root.insertBefore(note, form);
                else root.appendChild(note);
            }
        }
    }

    function createCard(item) {
        var li = document.createElement('li');
        li.className = 'kanban-card';

        var title = document.createElement('p');
        title.className = 'item-title';
        var hint = document.createElement('span');
        hint.className = 'drag-hint';
        hint.setAttribute('aria-hidden', 'true');
        hint.textContent = '⋮⋮';
        var idSpan = document.createElement('span');
        idSpan.className = 'item-id';
        var titleText = document.createElement('span');
        titleText.className = 'item-title-text';
        title.append(hint, idSpan, titleText);

        var meta = document.createElement('div');
        meta.className = 'item-meta';
        var badge = document.createElement('span');
        badge.className = 'badge';
        var project = document.createElement('span');
        project.className = 'item-project';
        meta.append(badge, project);

        var actions = document.createElement('div');
        actions.className = 'actions';
        var edit = document.createElement('a');
        edit.className = 'btn btn-secondary btn-sm';
        edit.href = '/Items/Edit/' + encodeURIComponent(item.id);
        edit.textContent = 'Edit';
        var form = document.createElement('form');
        form.method = 'post';
        form.action = location.pathname + '?handler=Delete';
        form.className = 'inline-form';
        form.addEventListener('submit', function (event) {
            if (!confirm('Delete this item?')) event.preventDefault();
        });
        var current = filters();
        form.append(
            tokenInput(),
            hidden('id', item.id),
            hidden('ProjectId', current.projectId == null ? '' : String(current.projectId)),
            hidden('Type', current.type || '')
        );
        var button = document.createElement('button');
        button.type = 'submit';
        button.className = 'btn btn-danger btn-sm';
        button.textContent = 'Delete';
        form.appendChild(button);
        actions.append(edit, form);

        li.append(title, meta, actions);
        return li;
    }

    function updateCard(card, item) {
        card.dataset.id = String(item.id);
        card.dataset.projectId = String(item.projectId);
        card.dataset.type = item.type;
        card.dataset.sortOrder = String(item.sortOrder);
        card.dataset.state = item.state;

        var idSpan = card.querySelector('.item-id');
        if (idSpan) idSpan.textContent = '#' + item.id;
        var title = card.querySelector('.item-title-text');
        if (title) title.textContent = item.title;

        var badge = card.querySelector('.badge');
        if (badge) {
            badge.textContent = item.type;
            badge.classList.toggle('badge-bug', item.type === 'Bug');
            badge.classList.toggle('badge-feature', item.type !== 'Bug');
        }

        var meta = card.querySelector('.item-meta');
        var project = card.querySelector('.item-project');
        if (!project && meta) {
            project = document.createElement('span');
            project.className = 'item-project';
            var desc = meta.querySelector('.item-desc');
            if (desc) meta.insertBefore(project, desc);
            else meta.appendChild(project);
        }
        if (project) {
            project.textContent = item.projectName || '';
            project.hidden = !!filters().projectId;
        }

        var descEl = card.querySelector('.item-desc');
        var description = item.description || '';
        var shown = description.length > 80 ? description.slice(0, 80) + '…' : description;
        if (shown.trim()) {
            if (!descEl && meta) {
                descEl = document.createElement('span');
                descEl.className = 'item-desc';
                meta.appendChild(descEl);
            }
            if (descEl) {
                descEl.hidden = false;
                descEl.textContent = shown;
            }
        } else if (descEl) {
            descEl.remove();
        }
    }

    function placeCard(card, item) {
        var column = Array.from(document.querySelectorAll('.kanban-column')).find(function (col) {
            return col.getAttribute('data-state') === item.state;
        });
        if (!column) {
            reload();
            return;
        }
        var list = column.querySelector('.kanban-cards');
        var key = [Number(item.projectId), Number(item.sortOrder), Number(item.id)];
        var cards = cardChildren(list).filter(function (other) { return other !== card; });
        var before = cards.find(function (other) { return compareKeys(key, cardKey(other)) < 0; }) || null;
        if (card.parentElement === list && nextCard(card) === before) return;
        if (before) list.insertBefore(card, before);
        else list.appendChild(card);
    }

    function createCommentItem(comment) {
        var li = document.createElement('li');
        li.dataset.commentId = String(comment.id);
        var main = document.createElement('div');
        main.className = 'comment-main';
        var body = document.createElement('p');
        body.className = 'comment-body';
        body.textContent = comment.body;
        var meta = document.createElement('p');
        meta.className = 'item-meta';
        meta.textContent = formatWhen(comment.createdAt);
        main.append(body, meta);

        var form = document.createElement('form');
        form.method = 'post';
        form.action = '/Items/Edit/' + encodeURIComponent(comment.workItemId) + '?handler=DeleteComment';
        form.className = 'inline-form';
        form.addEventListener('submit', function (event) {
            if (!confirm('Delete this comment?')) event.preventDefault();
        });
        form.append(
            tokenInput(),
            hidden('Id', comment.workItemId),
            hidden('commentId', comment.id)
        );
        var button = document.createElement('button');
        button.type = 'submit';
        button.className = 'btn btn-danger btn-sm';
        button.textContent = 'Delete';
        form.appendChild(button);
        li.append(main, form);
        return li;
    }

    function upsertProjectRow(project) {
        var form = document.querySelector('form.create-row');
        if (!form) return;
        var list = document.querySelector('.project-list');
        if (!list) {
            var empty = projectsEmptyMessage();
            if (empty) empty.remove();
            list = document.createElement('ul');
            list.className = 'project-list';
            form.after(list);
        }
        var row = findProjectRow(project.id);
        if (!row) {
            row = createProjectRow(project);
            list.appendChild(row);
        } else {
            var name = row.querySelector('.project-name');
            if (name) name.textContent = project.name;
            var input = row.querySelector('input[name="RenameName"]');
            if (input && document.activeElement !== input) input.value = project.name;
            if (input) input.setAttribute('aria-label', 'Rename ' + project.name);
            var del = row.querySelector('form.inline-form');
            if (del) del.dataset.confirm = deleteProjectMessage(project.name);
            setSlug(row, project.slug);
        }
        placeProjectRow(list, row);
    }

    function createProjectRow(project) {
        var li = document.createElement('li');
        li.dataset.projectId = String(project.id);
        var main = document.createElement('div');
        main.className = 'item-main';
        var name = document.createElement('div');
        name.className = 'project-name';
        name.textContent = project.name;
        main.appendChild(name);
        if (project.slug) {
            var slug = document.createElement('div');
            slug.className = 'project-slug';
            slug.textContent = project.slug;
            main.appendChild(slug);
        }

        var actions = document.createElement('div');
        actions.className = 'actions';
        var link = document.createElement('a');
        link.className = 'btn btn-secondary btn-sm';
        link.href = '/Items?projectId=' + encodeURIComponent(project.id);
        link.textContent = 'Items';

        var rename = document.createElement('form');
        rename.method = 'post';
        rename.action = '/Projects?handler=Rename';
        rename.className = 'inline-rename';
        var nameInput = document.createElement('input');
        nameInput.name = 'RenameName';
        nameInput.value = project.name;
        nameInput.required = true;
        nameInput.setAttribute('aria-label', 'Rename ' + project.name);
        var renameButton = document.createElement('button');
        renameButton.type = 'submit';
        renameButton.className = 'btn btn-secondary btn-sm';
        renameButton.textContent = 'Rename';
        rename.append(tokenInput(), hidden('id', project.id), nameInput, renameButton);

        var del = document.createElement('form');
        del.method = 'post';
        del.action = '/Projects?handler=Delete';
        del.className = 'inline-form';
        del.dataset.confirm = deleteProjectMessage(project.name);
        del.setAttribute('onsubmit', 'return confirm(this.dataset.confirm);');
        var deleteButton = document.createElement('button');
        deleteButton.type = 'submit';
        deleteButton.className = 'btn btn-danger btn-sm';
        deleteButton.textContent = 'Delete';
        del.append(tokenInput(), hidden('id', project.id), deleteButton);

        actions.append(link, rename, del);
        li.append(main, actions);
        return li;
    }

    function placeProjectRow(list, row) {
        var nameEl = row.querySelector('.project-name');
        var name = nameEl ? nameEl.textContent : '';
        var before = Array.from(list.children).find(function (el) {
            if (el === row) return false;
            var other = el.querySelector('.project-name');
            var text = other ? other.textContent : '';
            return text.localeCompare(name, undefined, { sensitivity: 'base' }) > 0;
        });
        if (before) list.insertBefore(row, before);
        else list.appendChild(row);
    }

    function setSlug(row, slug) {
        var main = row.querySelector('.item-main');
        var el = row.querySelector('.project-slug');
        if (!slug) {
            if (el) el.remove();
            return;
        }
        if (!el && main) {
            el = document.createElement('div');
            el.className = 'project-slug';
            main.appendChild(el);
        }
        if (el) el.textContent = slug;
    }

    function itemMatchesFilters(item) {
        var current = filters();
        if (current.projectId && item.projectId !== current.projectId) return false;
        if (current.type && item.type !== current.type) return false;
        return true;
    }

    function filters() {
        var project = document.querySelector('.filters select[name="ProjectId"]');
        var type = document.querySelector('.filters select[name="Type"]');
        return {
            projectId: project && project.value ? parseInt(project.value, 10) : null,
            type: type && type.value ? type.value : ''
        };
    }

    function refreshOrderedIdsFromDom() {
        var board = document.querySelector('.kanban-board');
        if (!board || !board.dataset.projectId) return;
        var cards = Array.from(board.querySelectorAll('.kanban-card'));
        cards.sort(function (a, b) { return compareKeys(cardKey(a), cardKey(b)); });
        board.dataset.orderedIds = cards.map(function (card) { return card.dataset.id; }).join(',');
    }

    function updateCounts() {
        document.querySelectorAll('.kanban-column').forEach(function (column) {
            var count = column.querySelector('.kanban-count');
            var list = column.querySelector('.kanban-cards');
            if (count && list) count.textContent = String(cardChildren(list).length);
        });
    }

    function syncEmpty() {
        var board = document.querySelector('.kanban-board');
        if (!board || !board.parentElement) return;
        var any = board.querySelector('.kanban-card');
        var empty = Array.from(board.parentElement.children).find(function (el) {
            return el.tagName === 'P' && el.classList.contains('empty');
        });
        if (!any && !empty) {
            var note = document.createElement('p');
            note.className = 'empty';
            note.textContent = 'No work items match these filters.';
            board.before(note);
        } else if (any && empty) {
            empty.remove();
        }
    }

    function normalizeChange(change) {
        var item = change.item || change.Item || null;
        var project = change.project || change.Project || null;
        var comment = change.comment || change.Comment || null;
        return {
            kind: normalizeKind(field(change, 'kind')),
            originClientId: field(change, 'originClientId') || '',
            workItemId: field(change, 'workItemId'),
            projectId: field(change, 'projectId'),
            commentId: field(change, 'commentId'),
            orderedIds: field(change, 'orderedIds') || null,
            orderedStates: field(change, 'orderedStates') || null,
            item: item ? normalizeItem(item) : null,
            project: project ? normalizeProject(project) : null,
            comment: comment ? normalizeComment(comment) : null
        };
    }

    function normalizeItem(item) {
        return {
            id: field(item, 'id'),
            projectId: field(item, 'projectId'),
            projectName: field(item, 'projectName') || '',
            type: normalizeType(field(item, 'type')),
            state: normalizeState(field(item, 'state')),
            title: field(item, 'title') || '',
            description: field(item, 'description') || '',
            sortOrder: field(item, 'sortOrder')
        };
    }

    function normalizeProject(project) {
        return {
            id: field(project, 'id'),
            name: field(project, 'name') || '',
            slug: field(project, 'slug') || ''
        };
    }

    function normalizeComment(comment) {
        return {
            id: field(comment, 'id'),
            workItemId: field(comment, 'workItemId'),
            body: field(comment, 'body') || '',
            createdAt: field(comment, 'createdAt')
        };
    }

    function field(obj, name) {
        if (!obj) return undefined;
        if (Object.prototype.hasOwnProperty.call(obj, name) && obj[name] != null) return obj[name];
        var pascal = name.charAt(0).toUpperCase() + name.slice(1);
        if (Object.prototype.hasOwnProperty.call(obj, pascal) && obj[pascal] != null) return obj[pascal];
        return undefined;
    }

    function normalizeKind(value) {
        if (typeof value === 'number' && kinds[value]) return kinds[value];
        var text = String(value == null ? '' : value);
        if (/^\d+$/.test(text) && kinds[Number(text)]) return kinds[Number(text)];
        if (!text) return '';
        return text.charAt(0).toUpperCase() + text.slice(1);
    }

    function normalizeType(value) {
        if (value === 1 || value === '1') return 'Bug';
        if (value === 0 || value === '0') return 'Feature';
        var text = String(value == null ? '' : value).toLowerCase();
        if (text === 'bug') return 'Bug';
        if (text === 'feature') return 'Feature';
        return String(value == null ? '' : value);
    }

    function normalizeState(value) {
        if (typeof value === 'number' && statesByNumber[value]) return statesByNumber[value];
        var text = String(value == null ? '' : value).replace(/\s+/g, ' ').trim();
        return stateAliases[text.toLowerCase()] || text;
    }

    function cardKey(card) {
        return [
            Number(card.dataset.projectId),
            Number(card.dataset.sortOrder),
            Number(card.dataset.id)
        ];
    }

    function compareKeys(a, b) {
        for (var i = 0; i < 3; i++) {
            if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
        }
        return 0;
    }

    function cardChildren(list) {
        return Array.from(list.children).filter(function (el) {
            return el.classList && el.classList.contains('kanban-card');
        });
    }

    function nextCard(card) {
        var next = card.nextElementSibling;
        while (next && !(next.classList && next.classList.contains('kanban-card')))
            next = next.nextElementSibling;
        return next;
    }

    function findCard(id) {
        var text = String(id);
        if (!/^\d+$/.test(text)) return null;
        return document.querySelector('.kanban-card[data-id="' + cssEscape(text) + '"]');
    }

    function findProjectRow(id) {
        var text = String(id);
        if (!/^\d+$/.test(text)) return null;
        return document.querySelector('.project-list li[data-project-id="' + cssEscape(text) + '"]');
    }

    function projectSelects() {
        return Array.from(document.querySelectorAll('select[name="ProjectId"], select[name="Input.ProjectId"]'));
    }

    function upsertProjectOption(select, project) {
        var existing = Array.from(select.options).find(function (option) {
            return option.value === String(project.id);
        });
        if (existing) {
            existing.textContent = project.name;
            return;
        }
        var option = document.createElement('option');
        option.value = String(project.id);
        option.textContent = project.name;
        var before = Array.from(select.options).find(function (candidate) {
            return candidate.value && candidate.textContent.localeCompare(project.name, undefined, { sensitivity: 'base' }) > 0;
        });
        if (before) select.insertBefore(option, before);
        else select.appendChild(option);
    }

    function commentsRoot() {
        return document.querySelector('.comments-section[data-work-item-id]');
    }

    function onEditItem(id) {
        var root = commentsRoot();
        return !!(root && String(root.dataset.workItemId) === String(id));
    }

    function pageShowsProjects() {
        return !!(document.querySelector('.kanban-board') || document.querySelector('form.create-row'));
    }

    function projectsEmptyMessage() {
        var form = document.querySelector('form.create-row');
        if (!form || !form.parentElement) return null;
        return Array.from(form.parentElement.children).find(function (el) {
            return el.tagName === 'P' && el.classList.contains('empty');
        }) || null;
    }

    function deleteProjectMessage(name) {
        return 'Delete project “' + name + '” and all its items?';
    }

    function tokenInput() {
        return hidden('__RequestVerificationToken', antiforgeryToken());
    }

    function antiforgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function hidden(name, value) {
        var input = document.createElement('input');
        input.type = 'hidden';
        input.name = name;
        input.value = value == null ? '' : String(value);
        return input;
    }

    function formatWhen(value) {
        var date = new Date(value);
        if (Number.isNaN(date.getTime())) return '';
        try {
            return date.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
        } catch (e) {
            return date.toLocaleString();
        }
    }

    function isDragging() {
        return !!document.querySelector('.sortable-chosen, .sortable-ghost');
    }

    function isSameOrigin(url) {
        if (url.startsWith('/')) return true;
        try { return new URL(url, location.origin).origin === location.origin; }
        catch (e) { return false; }
    }

    function cssEscape(value) {
        if (window.CSS && typeof CSS.escape === 'function') return CSS.escape(value);
        return String(value).replace(/["\\]/g, '\\$&');
    }

    function reload() {
        if (reloading) return;
        reloading = true;
        location.reload();
    }

    function go(url) {
        if (reloading) return;
        reloading = true;
        location.assign(url);
    }
})();
