// Bootstrap Icons(MIT) 스프라이트에서 아이콘 하나를 그린다. 이름은 https://icons.getbootstrap.com 의 이름 그대로다
import sprite from 'bootstrap-icons/bootstrap-icons.svg';

export function Icon({ name, label }: { name: string; label?: string }) {
  return (
    <svg className="i" role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : true}>
      <use href={`${sprite}#${name}`} />
    </svg>
  );
}
