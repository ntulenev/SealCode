fetch('/admin/snippets/manage/access', { cache: 'no-store' }).then(async response => {
  if (!response.ok || !(await response.json()).CanEdit) return;
  const navigation = document.querySelector('.admin-nav');
  if (!navigation) return;
  const link = document.createElement('a');
  link.href = '/admin/snippets/manage';
  link.textContent = 'Edit snippets';
  navigation.append(link);
}).catch(() => {});
