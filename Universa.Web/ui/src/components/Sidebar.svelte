<script>
  import { onMount, createEventDispatcher, onDestroy } from 'svelte';
  import TreeView from './TreeView.svelte';
  import FileToolbar from './FileToolbar.svelte';
  import { theme, toggleTheme } from '../stores/theme';
  import { logout, isAuthenticated, handleUnauthorized } from '../stores/auth';
  import { websocketService } from '../services/websocket-service';

  const dispatch = createEventDispatcher();

  let username = '';
  let isLoading = true;
  let isCollapsed = false;
  let sidebarWidth = 250;
  let isDragging = false;
  let startX;
  let startWidth;
  let selectedItem = null;

  function buildFileTree(files) {
    const root = {
      name: 'root',
      children: []
    };

    // Sort files by path to ensure parent directories come before children
    files.sort((a, b) => (a.path || a.Path).localeCompare(b.path || b.Path));

    files.forEach(file => {
      const path = (file.path || file.Path).replace(/\\/g, '/');
      const parts = path.split('/');
      let currentNode = root;

      for (let i = 0; i < parts.length; i++) {
        const part = parts[i];
        if (!part) continue;

        const isLastPart = i === parts.length - 1;
        const fullPath = parts.slice(0, i + 1).join('/');
        let node = currentNode.children.find(n => n.name === part);

        if (!node) {
          node = {
            name: part,
            path: fullPath,
            isDir: !isLastPart || file.isDir,
            icon: isLastPart && !file.isDir ? getFileIcon(part) : '📁',
            children: [],
            expanded: false
          };
          currentNode.children.push(node);
          
          // Sort children after adding new node
          currentNode.children.sort((a, b) => {
            // Directories come before files
            if (a.isDir !== b.isDir) {
              return a.isDir ? -1 : 1;
            }
            // Alphabetical sort within same type
            return a.name.localeCompare(b.name);
          });
        }
        currentNode = node;
      }
    });

    return root;
  }

  function getFileIcon(filename) {
    const ext = filename.split('.').pop().toLowerCase();
    switch (ext) {
      case 'md':
        return '📝';
      case 'todo':
        return '✓';
      default:
        return '📄';
    }
  }

  let treeItems = [
    {
      name: 'Services',
      icon: '🔌',
      expanded: false,
      isDir: true,
      children: []
    },
    {
      name: 'Library',
      icon: '📚',
      expanded: false,
      isDir: true,
      children: []
    }
  ];

  // Function to save expansion state
  function saveExpansionState(items) {
    const expansionState = {};
    function traverse(items, path = '') {
      items.forEach(item => {
        const itemPath = path ? `${path}/${item.name}` : item.name;
        if (item.isDir) {
          expansionState[itemPath] = item.expanded;
          if (item.children) {
            traverse(item.children, itemPath);
          }
        }
      });
    }
    traverse(items);
    localStorage.setItem('treeExpansionState', JSON.stringify(expansionState));
  }

  // Function to restore expansion state
  function restoreExpansionState(items) {
    const savedState = localStorage.getItem('treeExpansionState');
    if (!savedState) return items;

    const expansionState = JSON.parse(savedState);
    function traverse(items, path = '') {
      return items.map(item => {
        const itemPath = path ? `${path}/${item.name}` : item.name;
        if (item.isDir) {
          const expanded = expansionState[itemPath] ?? item.expanded;
          return {
            ...item,
            expanded,
            children: item.children ? traverse(item.children, itemPath) : []
          };
        }
        return item;
      });
    }
    return traverse(items);
  }

  async function loadFiles() {
    try {
      // Fetch file structure
      const filesResponse = await fetch('/api/files', {
        headers: {
          'X-Client': 'web'
        }
      });
      if (filesResponse.ok) {
        const response = await filesResponse.json();
        // Filter out .versions directories and their contents
        const filteredData = response.files.filter(file => {
          const path = (file.path || file.Path || '').replace(/\\/g, '/');
          const pathParts = path.split('/');
          // Exclude if any part of the path is .versions
          return !pathParts.some(part => part === '.versions');
        });

        // Build tree structure from filtered data
        const tree = buildFileTree(filteredData);

        // Update the Library section with the tree structure
        treeItems = treeItems.map(item => {
          if (item.name === 'Library') {
            return {
              ...item,
              children: tree.children || []
            };
          }
          return item;
        });
        // Restore expansion state after loading the file structure
        treeItems = restoreExpansionState(treeItems);
      } else if (filesResponse.status === 401) {
        handleUnauthorized();
      }
    } catch (error) {
      console.error('Error fetching data:', error);
      handleUnauthorized();
    }
  }

  onMount(async () => {
    try {
      // Fetch user info
      const userResponse = await fetch('/api/user');
      if (userResponse.ok) {
        const userData = await userResponse.json();
        username = userData.username;
        if (username === 'Guest') {
          handleUnauthorized();
          return;
        }
        isAuthenticated.set(true);
        await loadFiles();
        
        // Load saved sidebar width
        const savedWidth = localStorage.getItem('sidebarWidth');
        if (savedWidth) {
          sidebarWidth = parseInt(savedWidth, 10);
        }

        // Subscribe to file changes
        websocketService.subscribe('update', async (message) => {
          console.log('File updated:', message);
          await loadFiles();
        });

        websocketService.subscribe('delete', async (message) => {
          console.log('File deleted:', message);
          await loadFiles();
        });

        // Connect to WebSocket
        websocketService.connect();
      } else {
        handleUnauthorized();
      }
    } catch (error) {
      console.error('Error fetching data:', error);
      handleUnauthorized();
    } finally {
      isLoading = false;
    }
  });

  onDestroy(() => {
    websocketService.disconnect();
  });

  function handleSelect(event) {
    const item = event.detail;
    console.log('Selected item:', item);
    // Dispatch fileSelect event for files
    dispatch('fileSelect', item);
  }

  function handleToggle(event) {
    const { item, expanded } = event.detail;
    console.log('Toggle:', item.name, expanded);
    
    // Update the expanded state in our tree
    function updateExpanded(items) {
      return items.map(i => {
        if (i === item) {
          return { ...i, expanded };
        }
        if (i.children) {
          return { ...i, children: updateExpanded(i.children) };
        }
        return i;
      });
    }
    
    treeItems = updateExpanded(treeItems);
    // Save expansion state whenever it changes
    saveExpansionState(treeItems);
  }

  function handleSettings() {
    dispatch('settings');
  }

  async function handleLogout() {
    await logout();
  }

  function toggleCollapse() {
    isCollapsed = !isCollapsed;
  }

  function startResize(event) {
    isDragging = true;
    startX = event.clientX;
    startWidth = sidebarWidth;
    window.addEventListener('mousemove', handleResize);
    window.addEventListener('mouseup', stopResize);
    document.body.style.cursor = 'ew-resize';
    document.body.style.userSelect = 'none';
  }

  function handleResize(event) {
    if (!isDragging) return;
    const diff = event.clientX - startX;
    const newWidth = Math.max(200, Math.min(600, startWidth + diff));
    sidebarWidth = newWidth;
    // Save sidebar width whenever it changes
    localStorage.setItem('sidebarWidth', sidebarWidth.toString());
  }

  function stopResize() {
    isDragging = false;
    window.removeEventListener('mousemove', handleResize);
    window.removeEventListener('mouseup', stopResize);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }

  function handleItemClick(event) {
    selectedItem = event.detail;
    console.log('Selected item:', selectedItem);
  }

  async function handleNewFolder() {
    const folderName = prompt('Enter folder name:');
    if (!folderName) return;

    try {
      const response = await fetch('/api/directories', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          path: selectedItem?.isDir 
            ? `${selectedItem.path}/${folderName}`
            : folderName
        })
      });

      if (response.ok) {
        // Refresh the file list
        await loadFiles();
      } else {
        console.error('Failed to create folder');
      }
    } catch (err) {
      console.error('Error creating folder:', err);
    }
  }

  async function handleNewNote() {
    const noteName = prompt('Enter note name (without extension):');
    if (!noteName) return;

    const fileName = `${noteName}.md`;
    const path = selectedItem?.isDir 
      ? `${selectedItem.path}/${fileName}`
      : fileName;

    try {
      const response = await fetch(`/api/files/${path}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'text/plain'
        },
        body: `# ${noteName}\n\n`
      });

      if (response.ok) {
        await loadFiles();
      } else {
        console.error('Failed to create note');
      }
    } catch (err) {
      console.error('Error creating note:', err);
    }
  }

  async function handleDelete(item) {
    if (!confirm(`Are you sure you want to delete ${item.name}?`)) return;

    try {
      const response = await fetch(`/api/files/${item.path}`, {
        method: 'DELETE'
      });

      if (response.ok) {
        selectedItem = null;
        await loadFiles(); // Refresh the file list
      } else {
        const errorText = await response.text();
        console.error('Failed to delete item:', errorText);
        alert(`Failed to delete item: ${errorText}`);
      }
    } catch (err) {
      console.error('Error deleting item:', err);
      alert(`Error deleting item: ${err.message}`);
    }
  }
</script>

<div class="sidebar" class:collapsed={isCollapsed} style="width: {isCollapsed ? '50px' : sidebarWidth + 'px'}">
  {#if !isLoading && username && username !== 'Guest'}
    <button class="collapse-button" on:click={toggleCollapse} title={isCollapsed ? "Expand" : "Collapse"}>
      <span class="collapse-icon" class:collapsed={isCollapsed}>◀</span>
    </button>

    {#if !isCollapsed}
      <div class="title">UNIVERSA</div>
      <div class="tree-container">
        <div class="services-section">
          <div class="sidebar-section-title">Services</div>
          <TreeView 
            items={treeItems.filter(item => item.name === 'Services')} 
            {selectedItem}
            on:select={handleSelect} 
            on:toggle={handleToggle} 
            on:itemClick={handleItemClick} 
          />
        </div>
        
        <FileToolbar 
          {selectedItem} 
          on:newFolder={handleNewFolder}
          on:newNote={handleNewNote}
          on:delete={e => handleDelete(e.detail)}
        />
        
        <div class="library-section">
          <div class="sidebar-section-title">Library</div>
          <TreeView 
            items={treeItems.filter(item => item.name === 'Library')} 
            {selectedItem}
            on:select={handleSelect} 
            on:toggle={handleToggle} 
            on:itemClick={handleItemClick} 
          />
        </div>
      </div>

      <div class="user-section">
        <div class="user-info">
          <span class="username">{username}</span>
        </div>
        <div class="action-buttons">
          <button class="icon-button" on:click={toggleTheme} title="Toggle Dark Mode">
            <span class="icon">{$theme === 'dark' ? '🌙' : '☀️'}</span>
          </button>
          <button class="icon-button" on:click={handleSettings} title="Settings">
            <span class="icon">⚙️</span>
          </button>
          <button class="icon-button" on:click={handleLogout} title="Logout">
            <span class="icon">🚪</span>
          </button>
        </div>
      </div>
    {/if}

    <div class="resize-handle" on:mousedown={startResize}></div>
  {/if}
</div>

<style>
  .sidebar {
    position: relative;
    height: 100vh;
    background-color: var(--sidebar-bg);
    border-right: 1px solid var(--border-color);
    display: flex;
    flex-direction: column;
    transition: width 0.3s ease, background-color 0.3s ease;
  }

  .collapsed {
    width: 50px;
  }

  .collapse-button {
    position: absolute;
    top: 8px;
    right: 8px;
    width: 24px;
    height: 24px;
    padding: 0;
    background: none;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1;
  }

  .collapse-button:hover {
    background-color: rgba(0, 0, 0, 0.05);
  }

  .collapse-icon {
    font-size: 12px;
    transition: transform 0.3s ease;
  }

  .collapse-icon.collapsed {
    transform: rotate(180deg);
  }

  .title {
    font-family: system-ui, -apple-system, sans-serif;
    font-weight: 800;
    font-style: italic;
    font-size: 1.2rem;
    text-align: center;
    padding: 1rem 0.5rem;
    color: var(--text-color);
    letter-spacing: 0.05em;
    border-bottom: 1px solid var(--border-color);
    background-color: var(--section-bg);
  }

  .tree-container {
    flex: 1;
    overflow-y: auto;
    padding: 0.5rem;
    padding-right: 1rem;
    margin-top: 0;
  }

  .user-section {
    padding: 0.75rem 1rem;
    border-top: 1px solid var(--border-color);
    background-color: var(--section-bg);
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .user-info {
    display: flex;
    align-items: center;
  }

  .username {
    font-weight: 500;
    color: var(--text-color);
    margin-right: 0.5rem;
  }

  .action-buttons {
    display: flex;
    gap: 0.5rem;
  }

  .icon-button {
    background: none;
    border: none;
    padding: 0.5rem;
    cursor: pointer;
    border-radius: 4px;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: background-color 0.2s;
  }

  .icon-button:hover {
    background-color: var(--hover-color);
  }

  .icon {
    font-size: 1.2rem;
    line-height: 1;
  }

  .resize-handle {
    position: absolute;
    top: 0;
    right: -3px;
    width: 6px;
    height: 100%;
    cursor: ew-resize;
    background: transparent;
  }

  .resize-handle:hover {
    background: var(--hover-color);
  }

  .services-section,
  .library-section {
    margin-bottom: 0.5rem;
  }

  .sidebar-section-title {
    font-size: 0.75rem;
    font-weight: 600;
    text-transform: uppercase;
    color: var(--disabled-text);
    padding: 0.5rem;
    letter-spacing: 0.05em;
  }
</style> 