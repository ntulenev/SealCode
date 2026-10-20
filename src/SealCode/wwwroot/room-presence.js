/** Applies language and presence snapshots with independent monotonic revisions. */
export class RevisionedRoomView {
  constructor({ language, users }) {
    this.language = language;
    this.users = users;
    this.languageRevision = -1;
    this.presenceRevision = -1;
  }
  updateLanguage(language, revision) {
    if (!Number.isSafeInteger(revision) || revision < this.languageRevision) return false;
    this.languageRevision = revision;
    this.language(language);
    return true;
  }
  updateUsers(users, revision) {
    if (!Number.isSafeInteger(revision) || revision < this.presenceRevision) return false;
    this.presenceRevision = revision;
    this.users(users);
    return true;
  }
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
