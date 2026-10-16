export const ALLOWED_UPLOAD_EXTENSIONS = ['pdf', 'jpg', 'jpeg', 'png'] as const;

export const ACCEPT_UPLOAD_TYPES = '.pdf,.jpg,.jpeg,.png';

export const MAX_UPLOAD_BYTES = 5 * 1024 * 1024;

export interface UploadCandidate {
  name: string;
  size: number;
}

/**
 * Client-side mirror of the server document policy. The server remains
 * authoritative and re-validates content type, extension and signature; this
 * only spares the user a round-trip for obviously invalid files.
 *
 * Returns a human-readable message when the file is rejected, or null when it
 * is acceptable.
 */
export function validateUpload(file: UploadCandidate): string | null {
  const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
  if (!ALLOWED_UPLOAD_EXTENSIONS.includes(extension as (typeof ALLOWED_UPLOAD_EXTENSIONS)[number])) {
    return 'Upload a PDF, JPG or PNG file.';
  }
  if (file.size <= 0) {
    return 'The file appears to be empty.';
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return 'The file must be no larger than 5 MB.';
  }
  return null;
}
