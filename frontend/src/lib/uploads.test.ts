import { describe, expect, it } from 'vitest';
import { MAX_UPLOAD_BYTES, validateUpload } from './uploads';

const valid = { name: 'statement.pdf', size: 1024 };

describe('validateUpload', () => {
  it('accepts a small PDF', () => {
    expect(validateUpload(valid)).toBeNull();
  });

  it.each(['photo.jpg', 'scan.jpeg', 'id.PNG', 'doc.Pdf'])('accepts %s', (name) => {
    expect(validateUpload({ name, size: 1024 })).toBeNull();
  });

  it('rejects an unsupported extension', () => {
    expect(validateUpload({ name: 'notes.txt', size: 1024 })).toBe('Upload a PDF, JPG or PNG file.');
  });

  it('rejects a file with no extension', () => {
    expect(validateUpload({ name: 'README', size: 1024 })).toBe('Upload a PDF, JPG or PNG file.');
  });

  it('rejects an empty file', () => {
    expect(validateUpload({ name: 'blank.pdf', size: 0 })).toBe('The file appears to be empty.');
  });

  it('accepts a file exactly at the size limit', () => {
    expect(validateUpload({ name: 'big.pdf', size: MAX_UPLOAD_BYTES })).toBeNull();
  });

  it('rejects a file over the size limit', () => {
    expect(validateUpload({ name: 'huge.pdf', size: MAX_UPLOAD_BYTES + 1 })).toBe(
      'The file must be no larger than 5 MB.',
    );
  });

  it('checks the extension before the size', () => {
    expect(validateUpload({ name: 'huge.exe', size: MAX_UPLOAD_BYTES + 1 })).toBe(
      'Upload a PDF, JPG or PNG file.',
    );
  });
});
