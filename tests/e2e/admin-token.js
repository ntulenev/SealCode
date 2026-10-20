export async function csrf(request) {
  const response = await request.get('/admin/token');
  return { 'X-CSRF-TOKEN': (await response.json()).Token };
}
