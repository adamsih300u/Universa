<script>
    import { onMount } from 'svelte';
    import { files, connectionStatus, initializeWebSocket } from '$lib/stores/fileStore';
    import '../app.css';
    
    onMount(() => {
        initializeWebSocket();
    });
    
    let showLeftSidebar = true;
    let showRightSidebar = true;
</script>

<div class="min-h-screen bg-gray-100 text-gray-900">
    <!-- Original layout content -->
</div> 