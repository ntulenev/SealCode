/** Applies language and presence snapshots with independent monotonic revisions. */
export class RevisionedRoomView {
  /**
   * @param {{language: (language: string) => void, users: (users: string[]) => void}} options
   */
  constructor({ language, users }) {
    this.language = language;
    this.users = users;
    this.languageRevision = -1;
    /** @type {string | undefined} */
    this.currentLanguage = undefined;
    this.presenceRevision = -1;
  }
  /**
   * @param {string} language
   * @param {number} revision
   */
  updateLanguage(language, revision) {
    if (!Number.isSafeInteger(revision) || revision < this.languageRevision) return false;
    this.languageRevision = revision;
    this.currentLanguage = language;
    this.language(language);
    return true;
  }
  /**
   * @param {string[]} users
   * @param {number} revision
   */
  updateUsers(users, revision) {
    if (!Number.isSafeInteger(revision) || revision < this.presenceRevision) return false;
    this.presenceRevision = revision;
    this.users(users);
    return true;
  }
  /**
   * @param {import('./client-types.js').JoinRoomResult} result
   */
  apply(result) {
    this.updateLanguage(
      result.Language ?? result.language,
      Number(result.MetadataRevision ?? result.metadataRevision ?? 0)
    );
    this.updateUsers(
      result.Users ?? result.users ?? [],
      Number(result.PresenceRevision ?? result.presenceRevision ?? 0)
    );
  }
}
