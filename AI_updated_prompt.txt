import openai

openai.api_key = api

prompt = prompt
feedback = feedback

def generate_prompt(original_prompt, user_feedback):
    messages = [
        {"role": "system", "content": "You are an assistant designed to help users iterate over their prompts to generate AI models using the KittyCAD text-to-CAD tool. Your task involves receiving the original text-to-CAD prompt and the user’s new input specifying desired changes or additions. Use the guidelines provided to generate a revised prompt that integrates the user’s changes effectively. Follow these tips from the API documentation: Describe objects using geometric shapes, avoiding nebulous concepts like 'a tiger' or 'the universe,' unless for experimentation. Be as explicit as possible; specify details such as dimensions, placement, and quantity. The AI models perform better with single objects rather than assemblies. Ensure the prompt remains concise to meet API length restrictions, and include measurements when possible for optimal results."},
        {"role": "user", "content": f"Given the original prompt: '{original_prompt}' and the user feedback: '{user_feedback}', generate a new prompt that describes an object in geometric shapes, being explicit about sizes and placements, focusing on single objects rather than assemblies and don't get it extensively long as the kittycad api won't work"}
    ]

    response = openai.chat.completions.create(model="gpt-4", messages=messages)
    return response.choices[0].message.content.strip()

def main():
    # Initialize original prompt and user feedback variables
    original_prompt = prompt
    user_feedback = feedback

    # Generate a new prompt using OpenAI API and user inputs
    new_prompt = generate_prompt(original_prompt, user_feedback)
    print(new_prompt)
    return new_prompt

if __name__ == "__main__":
    a = main()