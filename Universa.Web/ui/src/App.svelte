<script>
  import { onMount, onDestroy } from "svelte";
  import Sidebar from "./components/Sidebar.svelte";
  import TabBar from "./components/TabBar.svelte";
  import TextEditor from "./components/TextEditor.svelte";
  import StatusBar from "./components/StatusBar.svelte";
  import { websocketService } from './services/websocket-service';
  import AIChatSidebar from "./components/AIChatSidebar.svelte";
  import { isAuthenticated, checkAuthStatus } from './stores/auth';
  import LoginModal from './components/LoginModal.svelte';
  import Settings from './components/Settings.svelte';
  import './app.css';

  let openTabs = [];
  let activeTab = null;
  let isChatSidebarCollapsed = false;
  let isLoading = true;
  let showLoginModal = false;

  // Function to save tabs state
  function saveTabsState() {
    if (!$isAuthenticated) return;
    
    const tabsState = {
      openTabs: openTabs.map(tab => ({
        name: tab.name,
        path: tab.path,
        icon: tab.icon,
        isDirty: tab.isDirty,
        content: tab.content,
        displayName: tab.displayName
      })),
      activeTabPath: activeTab?.path
    };
    localStorage.setItem('tabsState', JSON.stringify(tabsState));
  }

  // Function to restore tabs state
  async function restoreTabsState() {
    if (!$isAuthenticated) return;

    const savedState = localStorage.getItem('tabsState');
    if (!savedState) return;

    try {
      const { openTabs: savedTabs, activeTabPath } = JSON.parse(savedState);
      
      // Restore each tab and its content
      for (const tab of savedTabs) {
        try {
          // Only fetch content if it's not already in the saved state
          let content = tab.content;
          if (!content) {
            const response = await fetch(`/api/files/${encodeURIComponent(tab.path)}`);
            if (response.ok) {
              content = await response.text();
            }
          }
          const newTab = { 
            ...tab, 
            content,
            displayName: tab.isDirty ? tab.name + '*' : tab.name
          };
          openTabs = [...openTabs, newTab];
          
          // Restore active tab
          if (tab.path === activeTabPath) {
            activeTab = newTab;
          }
        } catch (error) {
          console.error('Error restoring tab:', tab.path, error);
        }
      }

      // If active tab wasn't restored but we have tabs, set the first one as active
      if (!activeTab && openTabs.length > 0) {
        activeTab = openTabs[0];
      }
    } catch (error) {
      console.error('Error parsing saved tabs state:', error);
      localStorage.removeItem('tabsState');
    }
  }

  onMount(async () => {
    try {
      const isAuthed = await checkAuthStatus();
      if (!isAuthed) {
        showLoginModal = true;
      } else {
        await restoreTabsState();
        websocketService.connect();
      }
    } finally {
      isLoading = false;
    }
  });

  onDestroy(() => {
    if ($isAuthenticated) {
      saveTabsState();
    }
    websocketService.destroy();
  });

  $: if ($isAuthenticated && !websocketService.isConnected) {
    websocketService.connect();
    restoreTabsState();
  } else if (!$isAuthenticated && websocketService.isConnected) {
    websocketService.destroy();
    showLoginModal = true;
  }

  async function handleFileSelect(event) {
    const file = event.detail;
    if (!file.isDir) {
      // Check if tab is already open
      const existingTab = openTabs.find(tab => tab.path === file.path);
      if (existingTab) {
        activeTab = { ...existingTab };
        return;
      }

      // Load file content
      try {
        const response = await fetch(`/api/files/${encodeURIComponent(file.path)}`);
        if (response.ok) {
          const content = await response.text();
          const newTab = {
            name: file.name,
            path: file.path,
            icon: file.icon,
            content,
            isDirty: false,
            displayName: file.name
          };
          // Update openTabs array with the new tab
          openTabs = [...openTabs, newTab];
          // Set the new tab as active
          activeTab = newTab;
          // Save the updated tabs state
          saveTabsState();
        }
      } catch (error) {
        console.error('Error loading file:', error);
      }
    }
  }

  function handleTabSelect(event) {
    const selectedTab = event.detail;
    // Find the tab in our openTabs array to get the latest state
    const tab = openTabs.find(t => t.path === selectedTab.path);
    if (tab) {
      // Create a new reference to trigger reactivity
      activeTab = { ...tab };
      saveTabsState();
    }
  }

  function handleTabClose(event) {
    const tab = event.detail;
    const index = openTabs.findIndex(t => t.path === tab.path);
    openTabs = openTabs.filter(t => t.path !== tab.path);
    
    // If we closed the active tab, activate another one
    if (activeTab.path === tab.path && openTabs.length > 0) {
      // Try to activate the tab to the left, or the first tab if we're at the beginning
      const newActiveTab = openTabs[Math.max(0, index - 1)];
      activeTab = { ...newActiveTab };
    } else if (openTabs.length === 0) {
      activeTab = null;
    }
    saveTabsState();
  }

  function handleDirtyStateChange(event) {
    const { isDirty } = event.detail;
    const displayName = isDirty ? activeTab.name + '*' : activeTab.name;
    
    // Update both the active tab and the tab in the openTabs array
    openTabs = openTabs.map(tab => 
      tab.path === activeTab.path 
        ? { ...tab, isDirty, displayName }
        : tab
    );
    
    // Create a new reference for the active tab to trigger reactivity
    activeTab = { ...activeTab, isDirty, displayName };
    saveTabsState();
  }

  function handleContentUpdate(event) {
    const { content, saved } = event.detail;
    const displayName = saved ? activeTab.name : activeTab.name + '*';
    const isDirty = !saved;
    
    // Update both the active tab and the tab in the openTabs array
    openTabs = openTabs.map(tab => 
      tab.path === activeTab.path 
        ? { ...tab, content, isDirty, displayName }
        : tab
    );
    
    // Create a new reference for the active tab to trigger reactivity
    activeTab = { ...activeTab, content, isDirty, displayName };
    saveTabsState();
  }

  function handleSettingsOpen() {
    // Check if settings tab is already open
    const existingTab = openTabs.find(tab => tab.path === 'settings');
    if (existingTab) {
      activeTab = { ...existingTab };
      return;
    }

    // Create new settings tab
    const newTab = {
      name: 'Settings',
      path: 'settings',
      icon: '⚙️',
      content: '',
      isDirty: false,
      displayName: 'Settings',
      isSettings: true
    };
    openTabs = [...openTabs, newTab];
    activeTab = newTab;
    saveTabsState();
  }
</script>

<main>
  {#if !isLoading}
    {#if $isAuthenticated}
      <div class="app-container">
        <Sidebar 
          on:fileSelect={handleFileSelect} 
          on:settings={handleSettingsOpen}
        />
        <div class="content-area">
          <TabBar tabs={openTabs} {activeTab} on:tabSelect={handleTabSelect} on:tabClose={handleTabClose} />
          <div class="editor-container">
            {#if activeTab}
              {#if activeTab.isSettings}
                <Settings on:close={() => handleTabClose({ detail: activeTab })} />
              {:else}
                <TextEditor
                  content={activeTab.content}
                  path={activeTab.path}
                  on:dirtyStateChange={handleDirtyStateChange}
                  on:contentUpdate={handleContentUpdate}
                />
              {/if}
            {:else}
              <div class="empty-state">
                <p>Select a file from the sidebar to start editing</p>
              </div>
            {/if}
          </div>
          <StatusBar />
        </div>
        <AIChatSidebar bind:isCollapsed={isChatSidebarCollapsed} />
      </div>
    {:else}
      <div class="login-container">
        <LoginModal bind:show={showLoginModal} />
      </div>
    {/if}
  {/if}
</main>

<style>
  .app-container {
    display: flex;
    height: 100vh;
    width: 100vw;
    overflow: hidden;
  }

  .content-area {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    min-width: 0;
  }

  .editor-container {
    flex: 1;
    overflow: hidden;
  }

  .empty-state {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--text-color);
    font-size: 16px;
  }

  .login-container {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100vh;
    width: 100vw;
    background: var(--background-color);
  }

  :global(body) {
    margin: 0;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen,
      Ubuntu, Cantarell, "Open Sans", "Helvetica Neue", sans-serif;
  }

  main {
    height: 100vh;
    width: 100vw;
  }
</style> 