(() => {
  window.powatchBlobUpload = {
    // Upload the actual captured JPEG frame (base64 data URL) to blob storage via SAS URL.
    async uploadFrame(sasUrl, dataUrl) {
      // Convert base64 data URL to a binary blob
      const res = await fetch(dataUrl);
      const blob = await res.blob();

      const response = await fetch(sasUrl, {
        method: 'PUT',
        headers: {
          'x-ms-blob-type': 'BlockBlob',
          'x-ms-blob-content-type': 'image/jpeg',
          'content-type': 'image/jpeg'
        },
        body: blob
      });

      if (!response.ok) {
        const details = await response.text();
        throw new Error(`Upload failed: ${response.status} ${details}`);
      }
    }
  };
})();
