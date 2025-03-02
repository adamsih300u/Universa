<script>
  import { onMount } from 'svelte';
  export let show = false;

  function handleEscape(event) {
    if (event.key === 'Escape') {
      show = false;
    }
  }

  onMount(() => {
    document.addEventListener('keydown', handleEscape);
    return () => document.removeEventListener('keydown', handleEscape);
  });
</script>

{#if show}
  <div class="modal-backdrop" on:click|self={() => show = false}>
    <div class="modal-content">
      <slot />
    </div>
  </div>
{/if}

<style>
  .modal-backdrop {
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1000;
  }

  .modal-content {
    background: var(--section-bg);
    padding: 2rem;
    border-radius: 0.5rem;
    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
    max-width: 90%;
    max-height: 90%;
    overflow: auto;
  }
</style> 