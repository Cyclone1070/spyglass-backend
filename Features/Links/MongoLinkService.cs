using MongoDB.Driver;

namespace spyglass_backend.Features.Links
{
	public class MongoLinkService(IMongoDatabase database)
	{
		private readonly IMongoCollection<Link> _linksCollection = database.GetCollection<Link>("links");

		public async Task<List<Link>> GetAsync() =>
			await _linksCollection.Find(_ => true).ToListAsync();

		public async Task<Link?> GetAsync(string id) =>
			await _linksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

		public async Task CreateAsync(Link newLink) =>
			await _linksCollection.InsertOneAsync(newLink);

		public async Task CreateManyAsync(IEnumerable<Link> newLinks) =>
			await _linksCollection.InsertManyAsync(newLinks);

		public async Task UpdateAsync(string id, Link updatedLink) =>
			await _linksCollection.ReplaceOneAsync(x => x.Id == id, updatedLink);

		public async Task RemoveAsync(string id) =>
			await _linksCollection.DeleteOneAsync(x => x.Id == id);
	}
}
