const handleNodeDoubleClick = (node: TreeNode) => {
  // Process only actual media service nodes
  if (node.type === 'service') {
    const service = mediaServices.find(s => s.id === node.id);
    if (service) {
      onServiceSelect(service);
    } else {
      console.error(`Unknown Media Service: ${node.id}`);
    }
  } else if (node.type === 'category') {
    // Category nodes should just expand/collapse, no need to process further
    return;
  }
}; 