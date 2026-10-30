// Keep both lists at 256 unique words: each random byte selects one word uniformly.
export const ADJECTIVES = Object.freeze(`
Able Active Agile Airy Alert Alive Amber Amiable
Awake Azure Balanced Beaming Beloved Blithe Blooming Blue
Bold Bouncy Brave Breezy Bright Brisk Bronze Bubbly
Busy Calm Candid Capable Careful Caring Casual Charmed
Cheery Chief Chilly Chipper Civil Classy Clean Clear
Clever Cloudy Cozy Crisp Cuddly Curious Dainty Dapper
Daring Dashing Dawn Dear Decent Deep Deft Dewy
Dreamy Dry Dusk Eager Early Earnest Earthy Easy
Fast Fearless Feisty Festive Fine Firm Fit Flashy
Fleet Floral Fluffy Flying Fond Foxy Free Fresh
Friendly Frosty Full Funky Furry Fuzzy Gallant Game
Gentle Gifted Glad Gleeful Gliding Glowing Golden Good
Graceful Grand Grateful Gray Green Groovy Happy Hardy
Hasty Hazy Hearty Helpful Heroic Hidden Honest Hopeful
Humble Hushed Icy Ideal Indigo Inner Ivory Jade
Jazzy Jolly Jovial Joyful Joyous Keen Kind Kindly
Lucid Lucky Lunar Lush Magic Merry Mild Mint
Misty Modern Mellow Modest Mossy Moving Musical Mystic
Navy Near Neat New Nice Nimble Noble Nocturnal
Orange Orderly Pacing Pale Patient Peaceful Peppy Perky
Petite Pink Placid Playful Plucky Plush Poised Polite
Proud Quick Quiet Quirky Radiant Rainy Rapid Ready
Regal Relaxed Rested Rich Rosy Round Royal Ruby
Ruddy Rugged Rustic Sage Sandy Satin Savvy Scarlet
Serene Shady Sharp Shiny Shy Silky Silver Simple
Sincere Sleek Smart Smiling Smooth Snappy Snug Snowy
Soft Solar Solid Sonic Sparkly Speedy Spirited Spry
Stable Starry Steady Stellar Still Stoic Stormy Stout
Strong Subtle Sunny Super Sure Sweet Swift Teal
Tender Tidy Tiny Toasty Tranquil True Trusty Upbeat
Valiant Velvet Verdant Vivid Warm Wavy Wild Wise
Witty Wondrous Woolly Worthy Young Zany Zesty Zippy
`.trim().split(/\s+/));

export const ANIMALS = Object.freeze(`
Fox Seal Otter Bear Wolf Lynx Puma Lion
Tiger Leopard Cheetah Jaguar Cougar Ocelot Caracal Serval
Bobcat Margay Oncilla Kodkod Cat Dog Dingo Dhole
Coyote Jackal Fennec Raccoon Badger Ferret Mink Weasel
Stoat Sable Marten Fisher Civet Genet Binturong Meerkat
Mongoose Coati Kinkajou Olingo Panda Koala Wombat Quokka
Wallaby Kangaroo Possum Opossum Glider Numbat Bandicoot Bilby
Rabbit Hare Pika Mouse Rat Vole Shrew Mole
Hamster Gerbil Dormouse Lemming Gopher Chipmunk Squirrel Marmot
Beaver Capybara Coypu Agouti Paca Cavy Chinchilla Porcupine
Hedgehog Tenrec Echidna Platypus Armadillo Pangolin Sloth Anteater
Aardvark Hyrax Bat Squid Lemur Loris Tarsier Galago
Monkey Macaque Mandrill Baboon Marmoset Tamarin Capuchin Gibbon
Gorilla Bonobo Chimp Yak Bison Buffalo Ox Cow
Frog Toad Newt Gecko Skink Snake Cobra Viper
Goat Sheep Ram Lamb Ibex Tahr Chamois Serow
Shark Ray Eel Pike Carp Cod Bass Trout
Deer Elk Moose Caribou Reindeer Wapiti Sika Muntjac
Pudu Giraffe Okapi Camel Llama Alpaca Vicuna Guanaco
Horse Pony Donkey Zebra Tapir Rhino Hippo Elephant
Pig Boar Warthog Peccary Dolphin Orca Whale Narwhal
Beluga Porpoise Dugong Manatee Walrus Turtle Penguin Puffin
Owl Eagle Hawk Falcon Kite Osprey Kestrel Harrier
Raven Crow Rook Jackdaw Jay Magpie Chough Starling
Sparrow Finch Robin Wren Lark Thrush Blackbird Oriole
Warbler Swallow Swift Martin Pipit Wagtail Bunting Canary
Siskin Linnet Goldfinch Bullfinch Grosbeak Crossbill Redpoll Waxwing
Dove Pigeon Parrot Macaw Cockatoo Budgie Lorikeet Toucan
Hornbill Hoopoe Shrike Kingfisher Roller Motmot Quetzal Trogon
Crane Heron Egret Ibis Stork Spoonbill Flamingo Pelican
Bee Ant Moth Wasp Cricket Beetle Cicada Mantis
Grebe Duck Goose Swan Teal Mallard Pintail Wigeon
`.trim().split(/\s+/));

/** Suggests a short name without storing or sharing identity across rooms.
 * @param {(values: Uint8Array) => Uint8Array} getRandomValues
 */
export function generateDisplayName(getRandomValues = (values) => crypto.getRandomValues(values)) {
  const indices = getRandomValues(new Uint8Array(2));
  return `${ADJECTIVES[indices[0]]} ${ANIMALS[indices[1]]}`;
}
