from openai import OpenAI
import base64

def encode_image(image_path):
    with open(image_path, 'rb') as image_file:
        return base64.b64encode(image_file.read()).decode('utf-8')

api_key = x
image_path = y

client = OpenAI(api_key=api_key)

base64_image = encode_image(image_path)

response = client.chat.completions.create(
    model='gpt-4o-mini',
    messages=[
        {
            'role': 'user',
            'content': [
                {
                    'type': 'text',
                    'text': 'Describe an object that can be modeled in CAD with simple operations, being as explicit as possible, using meassures if possible and focusing on single, self-contained items rather than assemblies. Maximum length 15 words.'
                },
                {
                    'type': 'image_url',
                    'image_url': {
                        'url': f'data:image/png;base64,{base64_image}'
                    },
                }
            ],
        },
    ],
    max_tokens=300,
)

# print('Completion Tokens:', response.usage.completion_tokens)
# print('Prompt Tokens:', response.usage.prompt_tokens)
# print('Total Tokens:', response.usage.total_tokens)

a = response.choices[0].message.content
print(a)